using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.MusicPlayer;
using Serilog;
using SystemMedia.Core;
using SystemMedia.Platform;

namespace SystemMedia.Players;

internal sealed class PlayersSource : IMediaSource
{
	private readonly PlayerWatcher[] _watchers;
	private readonly ILogger _logger;
	private readonly EventHandler _onPresenceChanged;
	private int _started;
	private int _disposed;

	internal PlayersSource(ILogger logger, TimeProvider time)
		: this([new MpvPlayer(), new SmPlayer()], logger, time)
	{
	}

	internal PlayersSource(IReadOnlyList<Player> players, ILogger logger, TimeProvider time)
	{
		_logger = logger.ForContext<PlayersSource>();
		_watchers = [.. players.Select(player => new PlayerWatcher(player, time, _logger))];
		_onPresenceChanged = (_, _) => SessionsChanged?.Invoke(this, EventArgs.Empty);
	}

	public event EventHandler? SessionsChanged;

	public bool IsStarted => Volatile.Read(ref _started) == 1;

	public Task StartAsync(CancellationToken cancellationToken)
	{
		if (Interlocked.Exchange(ref _started, 1) == 0)
		{
			foreach (var watcher in _watchers)
			{
				watcher.PresenceChanged += _onPresenceChanged;
				watcher.Start();
			}
		}

		return Task.CompletedTask;
	}

	public bool Owns(string appId) => appId.StartsWith(Player.AppIdPrefix, StringComparison.Ordinal);

	public IReadOnlyList<SessionCandidate> GetSessions() =>
		[.. _watchers
			.Select(watcher => watcher.Snapshot())
			.OfType<MediaSnapshot>()
			.Select(snapshot => new SessionCandidate(snapshot.AppId, snapshot.Status == PlaybackState.Playing))];

	public string? GetSystemCurrentAppId() => null;

	public Task<MediaSnapshot?> ReadAsync(string appId, CancellationToken cancellationToken) =>
		Task.FromResult(Find(appId)?.Snapshot());

	public Task<MusicPlayerArtwork?> ReadThumbnailAsync(string appId, CancellationToken cancellationToken) =>
		Find(appId) is { } watcher ? watcher.ReadCoverAsync(cancellationToken) : Task.FromResult<MusicPlayerArtwork?>(null);

	public Task<bool> SendAsync(string appId, MediaCommand command, CancellationToken cancellationToken) =>
		SendAsync(appId, command switch
		{
			MediaCommand.Play => PlayerCommand.Play,
			MediaCommand.Pause => PlayerCommand.Pause,
			MediaCommand.TogglePlayPause => PlayerCommand.Toggle,
			MediaCommand.Next => PlayerCommand.Next,
			_ => PlayerCommand.Previous,
		}, value: null, cancellationToken);

	public Task<bool> SeekAsync(string appId, TimeSpan position, CancellationToken cancellationToken) =>
		SendAsync(appId, PlayerCommand.Seek, position.TotalSeconds, cancellationToken);

	public Task<bool> SetShuffleAsync(string appId, bool enabled, CancellationToken cancellationToken) => Task.FromResult(false);

	public Task<bool> SetRepeatModeAsync(string appId, RepeatMode mode, CancellationToken cancellationToken) => Task.FromResult(false);

	// The executable path lets the system backend supply the icon and volume.
	public AppIdentity ResolveApp(string appId) =>
		Find(appId) is { } watcher
			? new AppIdentity(appId, watcher.Player.Name, AppLocation(watcher.ProcessPath), PackageFamilyName: null)
			: new AppIdentity(appId, appId[Player.AppIdPrefix.Length..], ExecutablePath: null, PackageFamilyName: null) { IsFallback = true };

	public bool HasAppIcon(AppIdentity app) => false;

	public Task<MusicPlayerArtwork?> GetAppIconAsync(AppIdentity app, CancellationToken cancellationToken) =>
		Task.FromResult<MusicPlayerArtwork?>(null);

	public int? GetVolumePercent(AppIdentity app) => Find(app.AppId)?.VolumePercent();

	public Task<bool> SetVolumePercentAsync(AppIdentity app, int volumePercent, CancellationToken cancellationToken) =>
		SendAsync(app.AppId, PlayerCommand.Volume, Math.Clamp(volumePercent, 0, 100), cancellationToken);

	public IReadOnlyList<IntegrationIssue> GetIssues() => [];

	public Task<IssueResolution?> ResolveIssueAsync(string issueId, CancellationToken cancellationToken) =>
		Task.FromResult<IssueResolution?>(null);

	// The host disposes the integration once per scope that holds it.
	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) == 1)
		{
			return;
		}

		var disposals = _watchers.Select(watcher =>
		{
			watcher.PresenceChanged -= _onPresenceChanged;
			return watcher.DisposeAsync().AsTask();
		}).ToArray();

		// A poll stuck on a hung player must not hold the exit up for long.
		Task.WaitAll(disposals, TimeSpan.FromSeconds(2));
	}

	internal static string? AppLocation(string? processPath)
	{
		const string BundleMarker = ".app/Contents/MacOS/";
		var bundleEnd = processPath?.IndexOf(BundleMarker, StringComparison.OrdinalIgnoreCase) ?? -1;
		return OperatingSystem.IsMacOS() && bundleEnd > 0 ? processPath![..(bundleEnd + 4)] : processPath;
	}

	private PlayerWatcher? Find(string appId) =>
		Array.Find(_watchers, watcher => string.Equals(watcher.Player.AppId, appId, StringComparison.Ordinal));

	private async Task<bool> SendAsync(string appId, PlayerCommand command, double? value, CancellationToken cancellationToken)
	{
		if (Find(appId) is not { } watcher)
		{
			return false;
		}

		try
		{
			return await watcher.SendAsync(command, value, cancellationToken);
		}
		catch (Exception exception) when (exception is IOException or TimeoutException or InvalidOperationException or HttpRequestException or System.ComponentModel.Win32Exception ||
			(exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
		{
			_logger.Warning("Player {Player} could not run {Command}: {Error}", watcher.Player.Name, command, exception.Message);
			return false;
		}
	}
}
