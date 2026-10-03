using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.MusicPlayer;
using SystemMedia.Core;
using SystemMedia.Platform;

namespace SystemMedia.Tests;

internal sealed class FakeMediaPlatform : IMediaPlatform
{
	internal Dictionary<string, MediaSnapshot> Sessions { get; } = new(StringComparer.Ordinal);

	internal string? SystemCurrent { get; set; }

	internal bool AcceptCommands { get; set; } = true;

	internal bool SupportsVolume { get; set; }

	internal List<(string AppId, MediaCommand Command)> Sent { get; } = [];

	internal Dictionary<string, IReadOnlyList<BrowserTab>> BrowserTabs { get; } = new(StringComparer.Ordinal);

	public event EventHandler? SessionsChanged
	{
		add { }
		remove { }
	}

	public bool IsStarted { get; set; } = true;

	public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	public IReadOnlyList<SessionCandidate> GetSessions() =>
		[.. Sessions.Values.Select(snapshot => new SessionCandidate(snapshot.AppId, snapshot.Status == PlaybackState.Playing))];

	public string? GetSystemCurrentAppId() => SystemCurrent;

	public Task<MediaSnapshot?> ReadAsync(string appId, CancellationToken cancellationToken) =>
		Task.FromResult(Sessions.GetValueOrDefault(appId));

	public Task<MusicPlayerArtwork?> ReadThumbnailAsync(string appId, CancellationToken cancellationToken) =>
		Task.FromResult(Sessions.TryGetValue(appId, out var snapshot) && snapshot.HasThumbnail
			? new MusicPlayerArtwork([0x89, 0x50, 0x4E, 0x47, (byte)appId.Length], "image/png")
			: null);

	public Task<IReadOnlyList<BrowserTab>?> ReadBrowserTabsAsync(string appId, CancellationToken cancellationToken) =>
		Task.FromResult(BrowserTabs.GetValueOrDefault(appId));

	public IReadOnlyList<string> GetBrowserWindowTitles(string appId) =>
		BrowserTabs.TryGetValue(appId, out var tabs) ? [.. tabs.Select(tab => tab.Title)] : [];

	public Task<bool> SendAsync(string appId, MediaCommand command, CancellationToken cancellationToken)
	{
		Sent.Add((appId, command));
		return Task.FromResult(AcceptCommands && Sessions.ContainsKey(appId));
	}

	public Task<bool> SeekAsync(string appId, TimeSpan position, CancellationToken cancellationToken) =>
		Task.FromResult(AcceptCommands && Sessions.ContainsKey(appId));

	public Task<bool> SetShuffleAsync(string appId, bool enabled, CancellationToken cancellationToken) =>
		Task.FromResult(AcceptCommands && Sessions.ContainsKey(appId));

	public Task<bool> SetRepeatModeAsync(string appId, RepeatMode mode, CancellationToken cancellationToken) =>
		Task.FromResult(AcceptCommands && Sessions.ContainsKey(appId));

	public AppIdentity ResolveApp(string appId) => new(appId, "App " + appId, "/apps/" + appId, PackageFamilyName: null);

	public bool HasAppIcon(AppIdentity app) => true;

	public Task<MusicPlayerArtwork?> GetAppIconAsync(AppIdentity app, CancellationToken cancellationToken) =>
		Task.FromResult<MusicPlayerArtwork?>(new MusicPlayerArtwork([1, 2, 3], "image/png"));

	public int? GetVolumePercent(AppIdentity app) => SupportsVolume ? 42 : null;

	public Task<bool> SetVolumePercentAsync(AppIdentity app, int volumePercent, CancellationToken cancellationToken) => Task.FromResult(SupportsVolume);

	public IReadOnlyList<IntegrationIssue> GetIssues() => [];

	public Task<IssueResolution?> ResolveIssueAsync(string issueId, CancellationToken cancellationToken) =>
		Task.FromResult<IssueResolution?>(null);

	public void Dispose()
	{
	}

	internal static MediaSnapshot Playing(string appId, string title, bool hasThumbnail = true) => new()
	{
		AppId = appId,
		AppName = string.Empty,
		Title = title,
		Status = PlaybackState.Playing,
		HasThumbnail = hasThumbnail,
		EndTime = TimeSpan.FromMinutes(3),
		Position = TimeSpan.FromSeconds(10),
		LastUpdatedTime = DateTimeOffset.UtcNow,
	};
}
