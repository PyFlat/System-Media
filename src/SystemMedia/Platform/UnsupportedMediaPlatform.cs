using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.MusicPlayer;
using SystemMedia.Core;

namespace SystemMedia.Platform;

internal sealed class UnsupportedMediaPlatform : IMediaPlatform
{
	internal const string UnsupportedIssueId = "platform-unsupported";

	public event EventHandler? SessionsChanged
	{
		add { }
		remove { }
	}

	public bool IsStarted => false;

	public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	public IReadOnlyList<SessionCandidate> GetSessions() => [];

	public string? GetSystemCurrentAppId() => null;

	public Task<MediaSnapshot?> ReadAsync(string appId, CancellationToken cancellationToken) => Task.FromResult<MediaSnapshot?>(null);

	public Task<MusicPlayerArtwork?> ReadThumbnailAsync(string appId, CancellationToken cancellationToken) =>
		Task.FromResult<MusicPlayerArtwork?>(null);

	public Task<bool> SendAsync(string appId, MediaCommand command, CancellationToken cancellationToken) => Task.FromResult(false);

	public Task<bool> SeekAsync(string appId, TimeSpan position, CancellationToken cancellationToken) => Task.FromResult(false);

	public Task<bool> SetShuffleAsync(string appId, bool enabled, CancellationToken cancellationToken) => Task.FromResult(false);

	public Task<bool> SetRepeatModeAsync(string appId, RepeatMode mode, CancellationToken cancellationToken) => Task.FromResult(false);

	public AppIdentity ResolveApp(string appId) => AppIdentity.Fallback(appId);

	public bool HasAppIcon(AppIdentity app) => false;

	public Task<MusicPlayerArtwork?> GetAppIconAsync(AppIdentity app, CancellationToken cancellationToken) =>
		Task.FromResult<MusicPlayerArtwork?>(null);

	public int? GetVolumePercent(AppIdentity app) => null;

	public Task<bool> SetVolumePercentAsync(AppIdentity app, int volumePercent, CancellationToken cancellationToken) => Task.FromResult(false);

	public IReadOnlyList<IntegrationIssue> GetIssues() =>
	[
		new IntegrationIssue
		{
			Id = UnsupportedIssueId,
			Title = Strings.Issues.PlatformUnsupported.Title(),
			Description = Strings.Issues.PlatformUnsupported.Description(),
			Severity = IntegrationIssueSeverity.Error,
		},
	];

	public Task<IssueResolution?> ResolveIssueAsync(string issueId, CancellationToken cancellationToken) =>
		Task.FromResult<IssueResolution?>(null);

	public void Dispose()
	{
	}
}
