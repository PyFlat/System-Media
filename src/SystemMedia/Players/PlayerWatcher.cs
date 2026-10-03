using System.Diagnostics;
using System.ComponentModel;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.MusicPlayer;
using Serilog;
using SystemMedia.Core;

namespace SystemMedia.Players;

// Polls one player and keeps its last reading, cover and executable path for the source.
internal sealed class PlayerWatcher : IAsyncDisposable
{
	// mpv front ends restart mpv for every file; the player must not vanish between two tracks.
	private static readonly TimeSpan _absenceGrace = TimeSpan.FromSeconds(5);
	private static readonly TimeSpan _processLookupInterval = TimeSpan.FromSeconds(5);

	private readonly TimeProvider _time;
	private readonly ILogger _logger;
	private readonly FailureEpisodeTracker _failures = new();
	private readonly SemaphoreSlim _wake = new(0, 1);
	private readonly SemaphoreSlim _coverGate = new(1, 1);
	private readonly CancellationTokenSource _stopping = new();

	private volatile Observation? _latest;
	private volatile CachedCover? _cover;
	private volatile string? _processPath;
	private DateTimeOffset _absentSince = DateTimeOffset.MaxValue;
	private DateTimeOffset _processLookedUpAt = DateTimeOffset.MinValue;
	private Task? _loop;

	internal PlayerWatcher(Player player, TimeProvider time, ILogger logger)
	{
		Player = player;
		_time = time;
		_logger = logger;
	}

	internal event EventHandler? PresenceChanged;

	internal Player Player { get; }

	internal bool IsPresent => _latest is not null;

	internal string? ProcessPath => _processPath;

	internal void Start() => _loop ??= Task.Run(() => RunAsync(_stopping.Token));

	internal MediaSnapshot? Snapshot()
	{
		if (_latest is not { } latest)
		{
			return null;
		}

		var reading = latest.Reading;
		return new MediaSnapshot
		{
			AppId = Player.AppId,
			AppName = Player.Name,
			Title = reading.Title,
			Artist = reading.Artist,
			AlbumTitle = reading.Album,
			HasThumbnail = reading.HasCover,
			Status = reading.State,
			PlaybackRate = 1,
			EndTime = reading.Duration ?? TimeSpan.Zero,
			Position = reading.Position ?? TimeSpan.Zero,
			LastUpdatedTime = latest.ReadAt,
		};
	}

	// Only when it can be set too: a slider the user cannot move is worse than none.
	internal int? VolumePercent() => Player.Supports(PlayerCommand.Volume) ? _latest?.Reading.VolumePercent : null;

	internal async Task<MusicPlayerArtwork?> ReadCoverAsync(CancellationToken cancellationToken)
	{
		if (_latest?.Reading is not { HasCover: true } reading)
		{
			return null;
		}

		if (_cover is { } cached && string.Equals(cached.TrackKey, reading.TrackKey, StringComparison.Ordinal))
		{
			return cached.Artwork;
		}

		await _coverGate.WaitAsync(cancellationToken);
		try
		{
			if (_cover is { } raced && string.Equals(raced.TrackKey, reading.TrackKey, StringComparison.Ordinal))
			{
				return raced.Artwork;
			}

			var artwork = await Player.ReadCoverAsync(cancellationToken);
			if (artwork is not null)
			{
				_cover = new CachedCover(reading.TrackKey, artwork);
			}

			return artwork;
		}
		finally
		{
			_coverGate.Release();
		}
	}

	internal async Task<bool> SendAsync(PlayerCommand command, double? value, CancellationToken cancellationToken)
	{
		var sent = await TrySendAsync(command, value, cancellationToken);
		if (sent is null)
		{
			var state = _latest?.Reading.State;
			sent = command switch
			{
				PlayerCommand.Toggle => await TrySendAsync(state == PlaybackState.Playing ? PlayerCommand.Pause : PlayerCommand.Play, null, cancellationToken),
				PlayerCommand.Play when state == PlaybackState.Playing => true,
				PlayerCommand.Pause when state != PlaybackState.Playing => true,
				PlayerCommand.Play or PlayerCommand.Pause => await TrySendAsync(PlayerCommand.Toggle, null, cancellationToken),
				_ => null,
			};
		}

		if (sent == true)
		{
			Wake();
		}

		return sent == true;
	}

	public async ValueTask DisposeAsync()
	{
		await _stopping.CancelAsync();
		if (_loop is { } loop)
		{
			await loop.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
		}

		await Player.DisposeAsync();
		_stopping.Dispose();
		_wake.Dispose();
		_coverGate.Dispose();
	}

	private async Task<bool?> TrySendAsync(PlayerCommand command, double? value, CancellationToken cancellationToken) =>
		Player.Supports(command) ? await Player.SendAsync(command, value, cancellationToken) : null;

	private void Wake()
	{
		try
		{
			_wake.Release();
		}
		catch (SemaphoreFullException)
		{
		}
	}

	private async Task RunAsync(CancellationToken cancellationToken)
	{
		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				await PollAsync(cancellationToken);
				await _wake.WaitAsync(Player.Interval, cancellationToken);
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
	}

	private async Task PollAsync(CancellationToken cancellationToken)
	{
		var now = _time.GetUtcNow();
		RefreshProcessPath(now);

		PlayerReading? reading;
		try
		{
			reading = await Player.ReadAsync(cancellationToken);
			if (_failures.RecordSuccess() is { } episode)
			{
				_logger.Information("Player {Player} can be read again after {Duration} ({Failures} failed reads).",
					Player.Name, episode.Duration, episode.Failures);
			}
		}
		catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
		{
			reading = null;
			LogFailure(exception);
		}

		var wasPresent = _latest is not null;
		if (reading is not null)
		{
			_absentSince = DateTimeOffset.MaxValue;
			_latest = new Observation(reading, _time.GetUtcNow());
		}
		else if (wasPresent)
		{
			if (_absentSince == DateTimeOffset.MaxValue)
			{
				_absentSince = now;
			}

			if (now - _absentSince >= _absenceGrace)
			{
				_latest = null;
			}
		}

		if (wasPresent != (_latest is not null))
		{
			_logger.Information("Player {Player} {Change}.", Player.Name, wasPresent ? "stopped" : "is playing");
			PresenceChanged?.Invoke(this, EventArgs.Empty);
		}
	}

	private void LogFailure(Exception exception)
	{
		var signal = _failures.RecordFailure(exception.Message);
		switch (signal.Kind)
		{
			case FailureEpisodeSignalKind.Onset:
				_logger.Warning("Player {Player} could not be read: {Error}", Player.Name, exception.Message);
				break;
			case FailureEpisodeSignalKind.SummaryDue:
				_logger.Information("Player {Player} still cannot be read after {Duration} ({Failures} reads): {LastError}",
					Player.Name, signal.Duration, signal.ConsecutiveFailures, signal.LastError);
				break;
			default:
				_logger.Debug("Player {Player} read failed: {LastError}", Player.Name, signal.LastError);
				break;
		}
	}

	private void RefreshProcessPath(DateTimeOffset now)
	{
		if (Player.ProcessName is not { } processName || now - _processLookedUpAt < _processLookupInterval)
		{
			return;
		}

		_processLookedUpAt = now;
		var processes = Process.GetProcessesByName(processName);
		try
		{
			string? path = null;
			foreach (var process in processes)
			{
				try
				{
					path = process.MainModule?.FileName;
				}
				catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or NotSupportedException)
				{
				}

				if (path is not null)
				{
					break;
				}
			}

			// Kept after the player closes: it still names the app and finds its icon.
			if (path is not null)
			{
				_processPath = path;
			}
		}
		finally
		{
			foreach (var process in processes)
			{
				process.Dispose();
			}
		}
	}

	private sealed record Observation(PlayerReading Reading, DateTimeOffset ReadAt);

	private sealed record CachedCover(string TrackKey, MusicPlayerArtwork Artwork);
}
