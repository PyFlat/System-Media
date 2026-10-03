using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.MusicPlayer;
using Serilog;
using SystemMedia.Core;
using SystemMedia.Players;
using Tmds.DBus.Protocol;

namespace SystemMedia.Platform.Linux;

internal sealed class LinuxMediaPlatform : IMediaPlatform
{
	internal const string UnavailableIssueId = "linux-media-unavailable";

	private static readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(1);
	private static readonly TimeSpan _callTimeout = TimeSpan.FromSeconds(3);
	private static readonly TimeSpan[] _reconnectDelays =
		[TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(60)];

	private readonly ILogger _logger;
	private readonly DesktopEntries _desktop = DesktopEntries.ForCurrentUser();
	private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5), MaxResponseContentBufferSize = CoverLoader.MaxBytes };
	private readonly Lock _gate = new();

	private volatile MprisBus? _bus;
	private volatile bool _startFailed;
	private volatile IReadOnlyDictionary<string, MprisPlayer> _players = new Dictionary<string, MprisPlayer>(StringComparer.Ordinal);
	private volatile string? _lastStarted;
	private CancellationTokenSource? _stopping;
	private Task? _loop;

	internal LinuxMediaPlatform(ILogger logger) => _logger = logger.ForContext<LinuxMediaPlatform>();

	public event EventHandler? SessionsChanged;

	public bool IsStarted => _bus is not null;

	public async Task StartAsync(CancellationToken cancellationToken)
	{
		if (_bus is null)
		{
			try
			{
				_bus = await MprisBus.ConnectAsync();
			}
			catch (Exception exception) when (exception is not OperationCanceledException)
			{
				_startFailed = true;
				throw;
			}

			_startFailed = false;
			await PollAsync();
		}

		lock (_gate)
		{
			if (_loop is null)
			{
				_stopping = new CancellationTokenSource();
				_loop = Task.Run(() => RunAsync(_stopping.Token), CancellationToken.None);
			}
		}
	}

	public IReadOnlyList<SessionCandidate> GetSessions() =>
	[
		.. _players.Values
			.GroupBy(player => player.AppId, StringComparer.Ordinal)
			.Select(group => new SessionCandidate(group.Key, group.Any(player => player.IsPlaying))),
	];

	// Linux has no system-wide current player; the last one to start playing stands in.
	public string? GetSystemCurrentAppId() =>
		_lastStarted is { } appId && _players.Values.Any(player => string.Equals(player.AppId, appId, StringComparison.Ordinal)) ? appId : null;

	public async Task<MediaSnapshot?> ReadAsync(string appId, CancellationToken cancellationToken) =>
		(await ReadPlayerAsync(appId, cancellationToken))?.ToSnapshot(DateTimeOffset.UtcNow);

	public async Task<MusicPlayerArtwork?> ReadThumbnailAsync(string appId, CancellationToken cancellationToken) =>
		Find(appId)?.ArtUrl is { Length: > 0 } url ? await CoverLoader.LoadAsync(url, _http, cancellationToken) : null;

	public async Task<bool> SendAsync(string appId, MediaCommand command, CancellationToken cancellationToken)
	{
		var (member, capability) = command switch
		{
			MediaCommand.Play => ("Play", "CanPlay"),
			MediaCommand.Pause => ("Pause", "CanPause"),
			MediaCommand.Next => ("Next", "CanGoNext"),
			MediaCommand.Previous => ("Previous", "CanGoPrevious"),
			_ => ("PlayPause", "CanPause"),
		};

		return await ReadPlayerAsync(appId, cancellationToken) is { } player && player.Can(capability) &&
			await TryAsync(bus => bus.CallAsync(player.BusName, member), cancellationToken);
	}

	// SetPosition needs a track id; without one the player is moved relative to where it is.
	public async Task<bool> SeekAsync(string appId, TimeSpan position, CancellationToken cancellationToken)
	{
		if (await ReadPlayerAsync(appId, cancellationToken) is not { } player || !player.Can("CanSeek"))
		{
			return false;
		}

		var target = (long)position.TotalMicroseconds;
		return player.TrackId is { } trackId
			? await TryAsync(bus => bus.SetPositionAsync(player.BusName, trackId, target), cancellationToken)
			: player.PositionMicros is { } current && await TryAsync(bus => bus.SeekByAsync(player.BusName, target - current), cancellationToken);
	}

	public async Task<bool> SetShuffleAsync(string appId, bool enabled, CancellationToken cancellationToken) =>
		await ReadPlayerAsync(appId, cancellationToken) is { } player && player.CanControl && player.Has("Shuffle") &&
		await TryAsync(bus => bus.SetAsync(player.BusName, "Shuffle", enabled), cancellationToken);

	public async Task<bool> SetRepeatModeAsync(string appId, RepeatMode mode, CancellationToken cancellationToken) =>
		await ReadPlayerAsync(appId, cancellationToken) is { } player && player.CanControl && player.Has("LoopStatus") &&
		await TryAsync(bus => bus.SetAsync(player.BusName, "LoopStatus", mode switch
		{
			RepeatMode.Track => "Track",
			RepeatMode.Context => "Playlist",
			_ => "None",
		}), cancellationToken);

	public AppIdentity ResolveApp(string appId) =>
		_players.Values.FirstOrDefault(player => string.Equals(player.AppId, appId, StringComparison.Ordinal)) is { Identity: { Length: > 0 } identity }
			? new AppIdentity(appId, identity, ExecutablePath: null, PackageFamilyName: null)
			: AppIdentity.Fallback(appId);

	public bool HasAppIcon(AppIdentity app) => IconPathOf(app.AppId) is not null;

	public Task<MusicPlayerArtwork?> GetAppIconAsync(AppIdentity app, CancellationToken cancellationToken) =>
		IconPathOf(app.AppId) is { } path ? DesktopEntries.LoadAsync(path, cancellationToken) : Task.FromResult<MusicPlayerArtwork?>(null);

	public int? GetVolumePercent(AppIdentity app) =>
		Find(app.AppId)?.Volume is { } volume ? (int)Math.Round(Math.Clamp(volume, 0, 1) * 100) : null;

	public async Task<bool> SetVolumePercentAsync(AppIdentity app, int volumePercent, CancellationToken cancellationToken) =>
		Find(app.AppId) is { } player && player.CanControl && player.Has("Volume") &&
		await TryAsync(bus => bus.SetAsync(player.BusName, "Volume", Math.Clamp(volumePercent, 0, 100) / 100d), cancellationToken);

	public IAudioDevices AudioDevices { get; } = new PulseAudioDevices();

	// Not "not started": the host asks for issues while the first start is still running.
	public IReadOnlyList<IntegrationIssue> GetIssues() =>
		!_startFailed ? [] :
		[
			new IntegrationIssue
			{
				Id = UnavailableIssueId,
				Title = Strings.Issues.LinuxMediaUnavailable.Title(),
				Description = Strings.Issues.LinuxMediaUnavailable.Description(),
				ActionLabel = Strings.Issues.LinuxMediaUnavailable.Action(),
				Severity = IntegrationIssueSeverity.Error,
			},
		];

	public async Task<IssueResolution?> ResolveIssueAsync(string issueId, CancellationToken cancellationToken)
	{
		if (!string.Equals(issueId, UnavailableIssueId, StringComparison.Ordinal))
		{
			return null;
		}

		try
		{
			await StartAsync(cancellationToken);
			return IssueResolution.Ok();
		}
		catch (Exception exception) when (IsBusFailure(exception))
		{
			_logger.Warning(exception, "The D-Bus session bus is still not available.");
			return IssueResolution.Failed(Strings.Issues.LinuxMediaUnavailable.StillUnavailable());
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

		_bus?.Dispose();
		_bus = null;
		_http.Dispose();
	}

	private string? IconPathOf(string appId) =>
		_players.Values.FirstOrDefault(player => string.Equals(player.AppId, appId, StringComparison.Ordinal)) is { } player
			? _desktop.IconPath(player.DesktopEntry, appId)
			: _desktop.IconPath(desktopEntry: null, appId);

	// A browser can run one player per window or tab; the one actually playing wins.
	private MprisPlayer? Find(string appId)
	{
		MprisPlayer? first = null;
		foreach (var player in _players.Values)
		{
			if (!string.Equals(player.AppId, appId, StringComparison.Ordinal))
			{
				continue;
			}

			if (player.IsPlaying)
			{
				return player;
			}

			first ??= player;
		}

		return first;
	}

	// MPRIS never signals Position, so every read asks the player.
	private async Task<MprisPlayer?> ReadPlayerAsync(string appId, CancellationToken cancellationToken)
	{
		if (Find(appId) is not { } known || _bus is not { } bus)
		{
			return null;
		}

		try
		{
			var properties = await bus.GetAllAsync(known.BusName, MprisBus.PlayerInterface).WaitAsync(_callTimeout, cancellationToken);
			return known with { Properties = properties };
		}
		catch (Exception exception) when (IsCallFailure(exception))
		{
			return null;
		}
		catch (Exception exception) when (IsBusFailure(exception))
		{
			Drop(bus, exception);
			return null;
		}
	}

	private async Task<bool> TryAsync(Func<MprisBus, Task> call, CancellationToken cancellationToken)
	{
		if (_bus is not { } bus)
		{
			return false;
		}

		try
		{
			await call(bus).WaitAsync(_callTimeout, cancellationToken);
			return true;
		}
		catch (Exception exception) when (IsCallFailure(exception))
		{
			_logger.Debug(exception, "An MPRIS call was refused.");
			return false;
		}
		catch (Exception exception) when (IsBusFailure(exception))
		{
			Drop(bus, exception);
			return false;
		}
	}

	private void Drop(MprisBus bus, Exception exception)
	{
		lock (_gate)
		{
			if (!ReferenceEquals(_bus, bus))
			{
				return;
			}

			_bus = null;
		}

		_logger.Warning(exception, "Lost the D-Bus session bus; reconnecting.");
		bus.Dispose();
		Replace(new Dictionary<string, MprisPlayer>(StringComparer.Ordinal));
	}

	private async Task RunAsync(CancellationToken cancellationToken)
	{
		using var timer = new PeriodicTimer(_pollInterval);
		var failures = 0;
		try
		{
			while (await timer.WaitForNextTickAsync(cancellationToken))
			{
				try
				{
					_bus ??= await MprisBus.ConnectAsync();
					await PollAsync();
					failures = 0;
					_startFailed = false;
				}
				catch (Exception exception) when (IsBusFailure(exception))
				{
					failures++;
					_startFailed = true;
					_logger.Warning(exception, "The D-Bus session bus is not reachable (attempt {Attempt}); trying again.", failures);
					if (_bus is { } broken)
					{
						Drop(broken, exception);
					}

					await Task.Delay(_reconnectDelays[Math.Min(failures - 1, _reconnectDelays.Length - 1)], cancellationToken);
				}
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
	}

	private async Task PollAsync()
	{
		if (_bus is not { } bus)
		{
			return;
		}

		var previous = _players;
		var current = new Dictionary<string, MprisPlayer>(StringComparer.Ordinal);
		foreach (var busName in await bus.ListPlayersAsync())
		{
			try
			{
				var root = previous.TryGetValue(busName, out var known) ? known : await ReadRootAsync(bus, busName);
				var properties = await bus.GetAllAsync(busName, MprisBus.PlayerInterface).WaitAsync(_callTimeout);
				current[busName] = root with { Properties = properties };
			}
			catch (Exception exception) when (IsCallFailure(exception))
			{
				// The player quit between listing and reading, or did not answer.
			}
		}

		foreach (var (busName, player) in current)
		{
			if (player.IsPlaying && !(previous.TryGetValue(busName, out var before) && before.IsPlaying))
			{
				_lastStarted = player.AppId;
			}
		}

		Replace(current);
	}

	private static async Task<MprisPlayer> ReadRootAsync(MprisBus bus, string busName)
	{
		var root = await bus.GetAllAsync(busName, MprisBus.RootInterface).WaitAsync(_callTimeout);
		return new MprisPlayer(
			busName,
			root.GetValueOrDefault("Identity") as string,
			root.GetValueOrDefault("DesktopEntry") as string,
			new Dictionary<string, object?>());
	}

	private void Replace(Dictionary<string, MprisPlayer> current)
	{
		var previous = _players;
		_players = current;
		if (!Same(previous, current))
		{
			SessionsChanged?.Invoke(this, EventArgs.Empty);
		}
	}

	private static bool Same(IReadOnlyDictionary<string, MprisPlayer> previous, Dictionary<string, MprisPlayer> current) =>
		previous.Count == current.Count &&
		current.All(entry => previous.TryGetValue(entry.Key, out var before) && before.IsPlaying == entry.Value.IsPlaying);

	private static bool IsCallFailure(Exception exception) =>
		exception is DBusErrorReplyException or DBusMessageException or DBusReadException or DBusUnexpectedValueException or TimeoutException;

	private static bool IsBusFailure(Exception exception) =>
		exception is DBusConnectionException or DBusConnectFailedException or DBusConnectionClosedException or InvalidOperationException or IOException;
}
