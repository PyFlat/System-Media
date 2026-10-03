using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.MusicPlayer;
using Serilog;
using SystemMedia.Core;
using SystemMedia.Platform.Windows.Audio;
using SystemMedia.Platform.Windows.Browser;
using SystemMedia.Platform.Windows.Shell;
using SystemMedia.Platform.Windows.Vlc;
using Windows.Foundation;
using Windows.Media;
using Windows.Media.Control;

namespace SystemMedia.Platform.Windows;

internal sealed class WindowsMediaPlatform : IMediaPlatform
{
	internal const string UnavailableIssueId = "media-controls-unavailable";
	internal const string VlcAddOnMissingIssueId = "vlc-add-on-missing";
	internal const string VlcAddOnDisabledIssueId = "vlc-add-on-disabled";

	private readonly SemaphoreSlim _startGate = new(1, 1);
	private readonly TypedEventHandler<GlobalSystemMediaTransportControlsSessionManager, SessionsChangedEventArgs> _onSessionsChanged;
	private readonly AppIdentityResolver _identities = new(TimeProvider.System);
	private readonly AppIconCache _icons = new();
	private readonly BrowserTabs _browserTabs = new(TimeProvider.System);
	private readonly ILogger _logger;

	private volatile GlobalSystemMediaTransportControlsSessionManager? _manager;

	// Not "not started": the host asks for issues while the first start is still running.
	private volatile bool _startFailed;

	internal WindowsMediaPlatform(ILogger logger)
	{
		_logger = logger.ForContext<WindowsMediaPlatform>();
		_onSessionsChanged = (_, _) => SessionsChanged?.Invoke(this, EventArgs.Empty);
	}

	public event EventHandler? SessionsChanged;

	public bool IsStarted => _manager is not null;

	public async Task StartAsync(CancellationToken cancellationToken)
	{
		if (_manager is not null)
		{
			return;
		}

		await _startGate.WaitAsync(cancellationToken);
		try
		{
			if (_manager is not null)
			{
				return;
			}

			GlobalSystemMediaTransportControlsSessionManager manager;
			try
			{
				manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().OnThreadPool(cancellationToken);
			}
			catch (Exception exception) when (exception is not OperationCanceledException)
			{
				_startFailed = true;
				throw;
			}

			manager.SessionsChanged += _onSessionsChanged;
			_manager = manager;
			_startFailed = false;
		}
		finally
		{
			_startGate.Release();
		}
	}

	public IReadOnlyList<SessionCandidate> GetSessions() =>
		_manager is { } manager
			?
			[
				.. manager.GetSessions()
					.Where(session => !string.IsNullOrEmpty(session.SourceAppUserModelId))
					.GroupBy(session => session.SourceAppUserModelId, StringComparer.Ordinal)
					.Select(group => new SessionCandidate(group.Key, group.Any(IsPlaying))),
			]
			: [];

	public string? GetSystemCurrentAppId() => _manager?.GetCurrentSession()?.SourceAppUserModelId;

	public async Task<MediaSnapshot?> ReadAsync(string appId, CancellationToken cancellationToken)
	{
		if (Find(appId) is not { } session)
		{
			return null;
		}

		try
		{
			// Read before the await: the session object must not be used after it.
			var playback = session.GetPlaybackInfo();
			var timeline = session.GetTimelineProperties();
			var properties = await session.TryGetMediaPropertiesAsync().OnThreadPool(cancellationToken);

			return new MediaSnapshot
			{
				AppId = appId,
				AppName = string.Empty,
				Title = properties?.Title,
				Artist = properties?.Artist,
				AlbumArtist = properties?.AlbumArtist,
				AlbumTitle = properties?.AlbumTitle,
				HasThumbnail = properties?.Thumbnail is not null,
				Status = playback.PlaybackStatus switch
				{
					GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => PlaybackState.Playing,
					GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => PlaybackState.Paused,
					_ => PlaybackState.Stopped,
				},
				PlaybackRate = playback.PlaybackRate,
				IsShuffleActive = playback.IsShuffleActive,
				RepeatMode = playback.AutoRepeatMode switch
				{
					MediaPlaybackAutoRepeatMode.Track => RepeatMode.Track,
					MediaPlaybackAutoRepeatMode.List => RepeatMode.Context,
					MediaPlaybackAutoRepeatMode.None => RepeatMode.Off,
					_ => null,
				},
				StartTime = timeline.StartTime,
				EndTime = timeline.EndTime,
				Position = timeline.Position,
				LastUpdatedTime = timeline.LastUpdatedTime,
			};
		}
		catch (COMException)
		{
			// The app closed its session between being found and being read.
			return null;
		}
	}

	public async Task<MusicPlayerArtwork?> ReadThumbnailAsync(string appId, CancellationToken cancellationToken)
	{
		if (Find(appId) is not { } session)
		{
			return null;
		}

		try
		{
			var properties = await session.TryGetMediaPropertiesAsync().OnThreadPool(cancellationToken);
			return properties?.Thumbnail is { } thumbnail ? await ArtworkReader.ReadAsync(thumbnail, cancellationToken) : null;
		}
		catch (COMException)
		{
			return null;
		}
	}

	public async Task<IReadOnlyList<BrowserTab>?> ReadBrowserTabsAsync(string appId, CancellationToken cancellationToken) =>
		BrowserOf(appId) is { } browser ? await _browserTabs.ReadAsync(browser, cancellationToken) : null;

	public IReadOnlyList<string> GetBrowserWindowTitles(string appId) =>
		BrowserOf(appId) is { } browser ? [.. BrowserWindows.Find(browser).Select(window => window.Title)] : [];

	// Some apps expose play and pause but not the toggle.
	public Task<bool> SendAsync(string appId, MediaCommand command, CancellationToken cancellationToken) =>
		RunAsync(appId, session => command switch
		{
			MediaCommand.Play => session.TryPlayAsync(),
			MediaCommand.Pause => session.TryPauseAsync(),
			MediaCommand.Next => session.TrySkipNextAsync(),
			MediaCommand.Previous => session.TrySkipPreviousAsync(),
			_ => session.GetPlaybackInfo() is var playback && playback.Controls.IsPlayPauseToggleEnabled
				? session.TryTogglePlayPauseAsync()
				: playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
					? session.TryPauseAsync()
					: session.TryPlayAsync(),
		}, cancellationToken);

	// The position is relative to the timeline's start time, which is not always zero.
	public Task<bool> SeekAsync(string appId, TimeSpan position, CancellationToken cancellationToken) =>
		RunAsync(appId, session =>
		{
			var timeline = session.GetTimelineProperties();
			var target = timeline.StartTime + position;
			if (timeline.EndTime > timeline.StartTime && target > timeline.EndTime)
			{
				target = timeline.EndTime;
			}

			return session.TryChangePlaybackPositionAsync(target.Ticks);
		}, cancellationToken);

	public Task<bool> SetShuffleAsync(string appId, bool enabled, CancellationToken cancellationToken) =>
		RunAsync(appId, session => session.TryChangeShuffleActiveAsync(enabled), cancellationToken);

	public Task<bool> SetRepeatModeAsync(string appId, RepeatMode mode, CancellationToken cancellationToken)
	{
		var repeat = mode switch
		{
			RepeatMode.Track => MediaPlaybackAutoRepeatMode.Track,
			RepeatMode.Context => MediaPlaybackAutoRepeatMode.List,
			_ => MediaPlaybackAutoRepeatMode.None,
		};

		return RunAsync(appId, session => session.TryChangeAutoRepeatModeAsync(repeat), cancellationToken);
	}

	public AppIdentity ResolveApp(string appId) => _identities.Resolve(appId);

	public bool HasAppIcon(AppIdentity app) => AppIconCache.HasIconSource(app);

	public Task<MusicPlayerArtwork?> GetAppIconAsync(AppIdentity app, CancellationToken cancellationToken) =>
		_icons.GetAsync(app, cancellationToken);

	public int? GetVolumePercent(AppIdentity app) => AppVolumeMixer.GetVolumePercent(app);

	public Task<bool> SetVolumePercentAsync(AppIdentity app, int volumePercent, CancellationToken cancellationToken) =>
		Task.FromResult(AppVolumeMixer.SetVolumePercent(app, volumePercent));

	public IReadOnlyList<IntegrationIssue> GetIssues()
	{
		var issues = new List<IntegrationIssue>();
		if (_startFailed)
		{
			issues.Add(new IntegrationIssue
			{
				Id = UnavailableIssueId,
				Title = Strings.Issues.MediaControlsUnavailable.Title(),
				Description = Strings.Issues.MediaControlsUnavailable.Description(),
				ActionLabel = Strings.Issues.MediaControlsUnavailable.Action(),
				Severity = IntegrationIssueSeverity.Error,
			});
		}

		if (VlcIssue() is { } vlc)
		{
			issues.Add(vlc);
		}

		return issues;
	}

	public async Task<IssueResolution?> ResolveIssueAsync(string issueId, CancellationToken cancellationToken)
	{
		switch (issueId)
		{
			case UnavailableIssueId:
				try
				{
					await StartAsync(cancellationToken);
					return IssueResolution.Ok();
				}
				catch (Exception exception) when (exception is not OperationCanceledException)
				{
					_logger.Warning(exception, "Windows media transport controls are still not available.");
					return IssueResolution.Failed(Strings.Issues.MediaControlsUnavailable.StillUnavailable());
				}

			case VlcAddOnMissingIssueId:
				return OpenVlcAddOnDownload();

			case VlcAddOnDisabledIssueId:
				return EnableVlcAddOn();

			default:
				return null;
		}
	}

	public void Dispose()
	{
		if (_manager is { } manager)
		{
			manager.SessionsChanged -= _onSessionsChanged;
			_manager = null;
		}

		_startGate.Dispose();
		_browserTabs.Dispose();
	}

	private BrowserUi? BrowserOf(string appId) =>
		ResolveApp(appId).ExecutableFileName is { } executable
			? BrowserUi.All.FirstOrDefault(browser => string.Equals(browser.ExecutableFileName, executable, StringComparison.OrdinalIgnoreCase))
			: null;

	private static bool IsPlaying(GlobalSystemMediaTransportControlsSession session) =>
		session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

	// A browser publishes one session per tab; the one actually playing wins.
	private GlobalSystemMediaTransportControlsSession? Find(string appId)
	{
		if (_manager is not { } manager)
		{
			return null;
		}

		GlobalSystemMediaTransportControlsSession? first = null;
		foreach (var session in manager.GetSessions())
		{
			if (!string.Equals(session.SourceAppUserModelId, appId, StringComparison.Ordinal))
			{
				continue;
			}

			if (IsPlaying(session))
			{
				return session;
			}

			first ??= session;
		}

		return first;
	}

	private async Task<bool> RunAsync(
		string appId,
		Func<GlobalSystemMediaTransportControlsSession, IAsyncOperation<bool>> command,
		CancellationToken cancellationToken)
	{
		if (Find(appId) is not { } session)
		{
			return false;
		}

		try
		{
			return await command(session).OnThreadPool(cancellationToken);
		}
		catch (COMException exception)
		{
			throw new MediaSessionNotFoundException(ResolveApp(appId).DisplayName, exception);
		}
	}

	private static IntegrationIssue? VlcIssue()
	{
		(VlcSetupState State, VlcInstallation? Installation) setup;
		try
		{
			setup = VlcSetup.Check();
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
		{
			return null;
		}

		return setup switch
		{
			(VlcSetupState.AddOnMissing, { } installation) => new IntegrationIssue
			{
				Id = VlcAddOnMissingIssueId,
				Title = Strings.Issues.VlcAddOnMissing.Title(),
				Description = Strings.Issues.VlcAddOnMissing.Description(installation.Architecture, installation.AddOnDirectory),
				ActionLabel = Strings.Issues.VlcAddOnMissing.Action(),
				Severity = IntegrationIssueSeverity.Info,
			},
			(VlcSetupState.AddOnDisabled, _) => new IntegrationIssue
			{
				Id = VlcAddOnDisabledIssueId,
				Title = Strings.Issues.VlcAddOnDisabled.Title(),
				Description = Strings.Issues.VlcAddOnDisabled.Description(),
				ActionLabel = Strings.Issues.VlcAddOnDisabled.Action(),
				Severity = IntegrationIssueSeverity.Info,
			},
			_ => null,
		};
	}

	private IssueResolution OpenVlcAddOnDownload()
	{
		try
		{
			using var browser = Process.Start(new ProcessStartInfo(VlcSetup.ReleasesUrl) { UseShellExecute = true });
			return IssueResolution.Ok(Strings.Issues.VlcAddOnMissing.Opened());
		}
		catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
		{
			_logger.Warning(exception, "Could not open the VLC add-on download page {Url}.", VlcSetup.ReleasesUrl);
			return IssueResolution.Failed(Strings.Issues.VlcAddOnMissing.OpenFailed(VlcSetup.ReleasesUrl));
		}
	}

	private IssueResolution EnableVlcAddOn()
	{
		try
		{
			VlcSetup.Enable();
			_logger.Information("Enabled the win10smtc control interface in {Path}.", VlcSetup.SettingsPath);
			return IssueResolution.Ok(Strings.Issues.VlcAddOnDisabled.Enabled());
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			_logger.Warning(exception, "Could not enable the VLC add-on in {Path}.", VlcSetup.SettingsPath);
			return IssueResolution.Failed(Strings.Issues.VlcAddOnDisabled.EnableFailed());
		}
	}
}
