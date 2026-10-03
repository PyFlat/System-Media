using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.MusicPlayer;
using SystemMedia.Core;

namespace SystemMedia.Platform;

internal enum MediaCommand
{
	Play,
	Pause,
	TogglePlayPause,
	Next,
	Previous,
}

// Reads return null and commands false when the app has no session; only a platform-level failure throws.
internal interface IMediaPlatform : IDisposable
{
	Task StartAsync(CancellationToken cancellationToken);

	bool IsStarted { get; }

	event EventHandler? SessionsChanged;

	IReadOnlyList<SessionCandidate> GetSessions();

	string? GetSystemCurrentAppId();

	Task<MediaSnapshot?> ReadAsync(string appId, CancellationToken cancellationToken);

	Task<MusicPlayerArtwork?> ReadThumbnailAsync(string appId, CancellationToken cancellationToken);

	// Can make the browser switch on its accessibility support, so only used by opt-in features.
	Task<IReadOnlyList<BrowserTab>?> ReadBrowserTabsAsync(string appId, CancellationToken cancellationToken) =>
		Task.FromResult<IReadOnlyList<BrowserTab>?>(null);

	// A browser's window titles, which name the page in front; read without asking the browser.
	IReadOnlyList<string> GetBrowserWindowTitles(string appId) => [];

	Task<bool> SendAsync(string appId, MediaCommand command, CancellationToken cancellationToken);

	Task<bool> SeekAsync(string appId, TimeSpan position, CancellationToken cancellationToken);

	Task<bool> SetShuffleAsync(string appId, bool enabled, CancellationToken cancellationToken);

	Task<bool> SetRepeatModeAsync(string appId, RepeatMode mode, CancellationToken cancellationToken);

	AppIdentity ResolveApp(string appId);

	bool HasAppIcon(AppIdentity app);

	Task<MusicPlayerArtwork?> GetAppIconAsync(AppIdentity app, CancellationToken cancellationToken);

	// null hides the widget's volume control.
	int? GetVolumePercent(AppIdentity app);

	Task<bool> SetVolumePercentAsync(AppIdentity app, int volumePercent, CancellationToken cancellationToken);

	IReadOnlyList<IntegrationIssue> GetIssues();

	Task<IssueResolution?> ResolveIssueAsync(string issueId, CancellationToken cancellationToken);
}
