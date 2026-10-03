using System.Diagnostics;
using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.MusicPlayer;
using Serilog;
using SystemMedia.Core;

namespace SystemMedia.Platform.MacOS;

internal sealed class MacMediaPlatform : IMediaPlatform
{
	internal const string UnavailableIssueId = "macos-media-unavailable";
	internal const string MediaControlMissingIssueId = "macos-media-control-missing";

	private static readonly TimeSpan[] _restartDelays =
		[TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(60)];

	private const int FailuresBeforeIssue = 3;

	// From media-control's help.
	private const int SendPlay = 0;
	private const int SendPause = 1;
	private const int SendTogglePlayPause = 2;
	private const int SendNext = 4;
	private const int SendPrevious = 5;
	private const int ModeOff = 1;
	private const int ShuffleTracks = 3;
	private const int RepeatTrack = 2;
	private const int RepeatPlaylist = 3;

	private readonly NowPlayingState _state = new();
	private readonly MacAppCatalog _apps = new();
	private readonly ILogger _logger;
	private readonly Lock _gate = new();

	private CancellationTokenSource? _stopping;
	private Task? _loop;
	private volatile bool _started;
	private volatile bool _broken;
	private volatile bool _missing;
	private int _consecutiveFailures;

	internal MacMediaPlatform(ILogger logger)
	{
		_logger = logger.ForContext<MacMediaPlatform>();
		_apps.Resolved += (_, _) => SessionsChanged?.Invoke(this, EventArgs.Empty);
	}

	public event EventHandler? SessionsChanged;

	public bool IsStarted => _started && !_broken;

	public Task StartAsync(CancellationToken cancellationToken)
	{
		_missing = MediaControl.Locate() is null;
		if (_missing)
		{
			_broken = true;
			throw new InvalidOperationException($"media-control is not installed (looked in {string.Join(", ", MediaControl.SearchPaths)}).");
		}

		lock (_gate)
		{
			_broken = false;
			_started = true;
			Interlocked.Exchange(ref _consecutiveFailures, 0);
			if (_loop is null)
			{
				_stopping = new CancellationTokenSource();
				_loop = Task.Run(() => RunStreamAsync(_stopping.Token), CancellationToken.None);
			}
		}

		return Task.CompletedTask;
	}

	public IReadOnlyList<SessionCandidate> GetSessions() =>
		_state.Current is { } current ? [new SessionCandidate(current.AppId, current.Playing)] : [];

	public string? GetSystemCurrentAppId() => _state.Current?.AppId;

	public Task<MediaSnapshot?> ReadAsync(string appId, CancellationToken cancellationToken) =>
		Task.FromResult(NowPlaying(appId)?.ToSnapshot());

	public Task<MusicPlayerArtwork?> ReadThumbnailAsync(string appId, CancellationToken cancellationToken) =>
		Task.FromResult(NowPlaying(appId)?.DecodeArtwork());

	public Task<bool> SendAsync(string appId, MediaCommand command, CancellationToken cancellationToken) =>
		NowPlaying(appId) is null
			? Task.FromResult(false)
			: MediaControl.SendAsync(command switch
			{
				MediaCommand.Play => SendPlay,
				MediaCommand.Pause => SendPause,
				MediaCommand.Next => SendNext,
				MediaCommand.Previous => SendPrevious,
				_ => SendTogglePlayPause,
			}, cancellationToken);

	public Task<bool> SeekAsync(string appId, TimeSpan position, CancellationToken cancellationToken) =>
		NowPlaying(appId) is null ? Task.FromResult(false) : MediaControl.SeekAsync(position, cancellationToken);

	public Task<bool> SetShuffleAsync(string appId, bool enabled, CancellationToken cancellationToken) =>
		NowPlaying(appId) is null ? Task.FromResult(false) : MediaControl.SetShuffleAsync(enabled ? ShuffleTracks : ModeOff, cancellationToken);

	public Task<bool> SetRepeatModeAsync(string appId, RepeatMode mode, CancellationToken cancellationToken) =>
		NowPlaying(appId) is null
			? Task.FromResult(false)
			: MediaControl.SetRepeatAsync(mode switch
			{
				RepeatMode.Track => RepeatTrack,
				RepeatMode.Context => RepeatPlaylist,
				_ => ModeOff,
			}, cancellationToken);

	public AppIdentity ResolveApp(string appId) => _apps.Resolve(appId);

	public bool HasAppIcon(AppIdentity app) => MacAppCatalog.HasIcon(app);

	public Task<MusicPlayerArtwork?> GetAppIconAsync(AppIdentity app, CancellationToken cancellationToken) =>
		_apps.GetIconAsync(app, cancellationToken);

	public int? GetVolumePercent(AppIdentity app) => null;

	public Task<bool> SetVolumePercentAsync(AppIdentity app, int volumePercent, CancellationToken cancellationToken) => Task.FromResult(false);

	// Only once a start failed or the stream broke: the host asks for issues while the first start still runs.
	public IReadOnlyList<IntegrationIssue> GetIssues() =>
		!_broken ? []
		: _missing ?
		[
			new IntegrationIssue
			{
				Id = MediaControlMissingIssueId,
				Title = Strings.Issues.MediaControlMissing.Title(),
				Description = Strings.Issues.MediaControlMissing.Description(MediaControl.InstallCommand),
				ActionLabel = Strings.Issues.MediaControlMissing.Action(),
				Severity = IntegrationIssueSeverity.Error,
			},
		]
		:
		[
			new IntegrationIssue
			{
				Id = UnavailableIssueId,
				Title = Strings.Issues.MacMediaUnavailable.Title(),
				Description = Strings.Issues.MacMediaUnavailable.Description(),
				ActionLabel = Strings.Issues.MacMediaUnavailable.Action(),
				Severity = IntegrationIssueSeverity.Error,
			},
		];

	public async Task<IssueResolution?> ResolveIssueAsync(string issueId, CancellationToken cancellationToken)
	{
		if (!string.Equals(issueId, UnavailableIssueId, StringComparison.Ordinal) &&
			!string.Equals(issueId, MediaControlMissingIssueId, StringComparison.Ordinal))
		{
			return null;
		}

		try
		{
			await StartAsync(cancellationToken);
			return IssueResolution.Ok();
		}
		catch (InvalidOperationException exception)
		{
			_logger.Warning(exception, "macOS Now Playing is still not available.");
			return IssueResolution.Failed(_missing
				? Strings.Issues.MediaControlMissing.StillMissing(MediaControl.InstallCommand)
				: Strings.Issues.MacMediaUnavailable.StillUnavailable());
		}
	}

	public void Dispose()
	{
		lock (_gate)
		{
			_stopping?.Cancel();
			_stopping?.Dispose();
			_stopping = null;
			_loop = null;
		}
	}

	private NowPlayingInfo? NowPlaying(string appId) =>
		_state.Current is { } current && string.Equals(current.AppId, appId, StringComparison.Ordinal) ? current : null;

	private async Task RunStreamAsync(CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			var receivedData = false;
			try
			{
				using var process = MediaControl.StartStream();
				using var stop = cancellationToken.Register(() => TryKill(process));
				_ = LogErrorsAsync(process);

				while (await process.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
				{
					if (line.Length == 0)
					{
						continue;
					}

					receivedData = true;
					Interlocked.Exchange(ref _consecutiveFailures, 0);
					_broken = false;
					if (Apply(line))
					{
						SessionsChanged?.Invoke(this, EventArgs.Empty);
					}
				}

				await process.WaitForExitAsync(cancellationToken);
				_logger.Warning("media-control stream exited with code {ExitCode}.", process.ExitCode);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				return;
			}
			catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
			{
				_logger.Warning(exception, "media-control stream failed.");
			}

			_state.Clear();
			SessionsChanged?.Invoke(this, EventArgs.Empty);

			var failures = receivedData ? 1 : Interlocked.Increment(ref _consecutiveFailures);
			if (failures >= FailuresBeforeIssue && !_broken)
			{
				_broken = true;
				_logger.Error("media-control keeps exiting without reporting anything; macOS Now Playing is unavailable.");
			}

			try
			{
				await Task.Delay(_restartDelays[Math.Min(failures - 1, _restartDelays.Length - 1)], cancellationToken);
			}
			catch (OperationCanceledException)
			{
				return;
			}
		}
	}

	private bool Apply(string line)
	{
		try
		{
			return _state.Apply(line);
		}
		catch (System.Text.Json.JsonException exception)
		{
			_logger.Debug(exception, "Ignored an unreadable media-control line.");
			return false;
		}
	}

	private async Task LogErrorsAsync(Process process)
	{
		try
		{
			while (await process.StandardError.ReadLineAsync() is { } line)
			{
				if (line.Length > 0)
				{
					_logger.Debug("media-control: {Message}", line);
				}
			}
		}
		catch (Exception exception) when (exception is IOException or InvalidOperationException or ObjectDisposedException)
		{
		}
	}

	private static void TryKill(Process process)
	{
		try
		{
			process.Kill(entireProcessTree: true);
		}
		catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
		{
		}
	}
}
