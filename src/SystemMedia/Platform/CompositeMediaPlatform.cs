using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.MusicPlayer;
using SystemMedia.Core;

namespace SystemMedia.Platform;

internal sealed class CompositeMediaPlatform : IMediaPlatform
{
	private readonly IMediaPlatform _system;
	private readonly IReadOnlyList<IMediaSource> _sources;
	private readonly EventHandler _forwardSessionsChanged;

	internal CompositeMediaPlatform(IMediaPlatform system, IReadOnlyList<IMediaSource> sources)
	{
		_system = system;
		_sources = sources;
		_forwardSessionsChanged = (_, _) => SessionsChanged?.Invoke(this, EventArgs.Empty);
		_system.SessionsChanged += _forwardSessionsChanged;
		foreach (var source in _sources)
		{
			source.SessionsChanged += _forwardSessionsChanged;
		}
	}

	public event EventHandler? SessionsChanged;

	internal IMediaPlatform System => _system;

	public bool IsStarted => _system.IsStarted || _sources.Any(source => source.IsStarted);

	// Sources start first so a failing system backend does not keep them from running.
	public async Task StartAsync(CancellationToken cancellationToken)
	{
		foreach (var source in _sources)
		{
			await source.StartAsync(cancellationToken);
		}

		await _system.StartAsync(cancellationToken);
	}

	public IReadOnlyList<SessionCandidate> GetSessions()
	{
		var sessions = _system.GetSessions();
		return _sources.Count == 0 ? sessions : [.. sessions, .. _sources.SelectMany(source => source.GetSessions())];
	}

	public string? GetSystemCurrentAppId() => _system.GetSystemCurrentAppId();


	public Task<MediaSnapshot?> ReadAsync(string appId, CancellationToken cancellationToken) =>
		For(appId).ReadAsync(appId, cancellationToken);

	public Task<MusicPlayerArtwork?> ReadThumbnailAsync(string appId, CancellationToken cancellationToken) =>
		For(appId).ReadThumbnailAsync(appId, cancellationToken);

	public Task<IReadOnlyList<BrowserTab>?> ReadBrowserTabsAsync(string appId, CancellationToken cancellationToken) =>
		For(appId).ReadBrowserTabsAsync(appId, cancellationToken);

	public IReadOnlyList<string> GetBrowserWindowTitles(string appId) => For(appId).GetBrowserWindowTitles(appId);

	public Task<bool> SendAsync(string appId, MediaCommand command, CancellationToken cancellationToken) =>
		For(appId).SendAsync(appId, command, cancellationToken);

	public Task<bool> SeekAsync(string appId, TimeSpan position, CancellationToken cancellationToken) =>
		For(appId).SeekAsync(appId, position, cancellationToken);

	public Task<bool> SetShuffleAsync(string appId, bool enabled, CancellationToken cancellationToken) =>
		For(appId).SetShuffleAsync(appId, enabled, cancellationToken);

	public Task<bool> SetRepeatModeAsync(string appId, RepeatMode mode, CancellationToken cancellationToken) =>
		For(appId).SetRepeatModeAsync(appId, mode, cancellationToken);

	public AppIdentity ResolveApp(string appId) => For(appId).ResolveApp(appId);

	public bool HasAppIcon(AppIdentity app) => For(app.AppId).HasAppIcon(app) || _system.HasAppIcon(app);

	public Task<MusicPlayerArtwork?> GetAppIconAsync(AppIdentity app, CancellationToken cancellationToken)
	{
		var owner = For(app.AppId);
		return owner.HasAppIcon(app) ? owner.GetAppIconAsync(app, cancellationToken) : _system.GetAppIconAsync(app, cancellationToken);
	}

	public int? GetVolumePercent(AppIdentity app)
	{
		var owner = For(app.AppId);
		return owner.GetVolumePercent(app) ?? (ReferenceEquals(owner, _system) ? null : _system.GetVolumePercent(app));
	}

	public Task<bool> SetVolumePercentAsync(AppIdentity app, int volumePercent, CancellationToken cancellationToken)
	{
		var owner = For(app.AppId);
		return owner.GetVolumePercent(app) is not null || ReferenceEquals(owner, _system)
			? owner.SetVolumePercentAsync(app, volumePercent, cancellationToken)
			: _system.SetVolumePercentAsync(app, volumePercent, cancellationToken);
	}

	public IReadOnlyList<IntegrationIssue> GetIssues() =>
		[.. _system.GetIssues(), .. _sources.SelectMany(source => source.GetIssues())];

	public async Task<IssueResolution?> ResolveIssueAsync(string issueId, CancellationToken cancellationToken)
	{
		if (await _system.ResolveIssueAsync(issueId, cancellationToken) is { } resolution)
		{
			return resolution;
		}

		foreach (var source in _sources)
		{
			if (await source.ResolveIssueAsync(issueId, cancellationToken) is { } resolved)
			{
				return resolved;
			}
		}

		return null;
	}

	public void Dispose()
	{
		_system.SessionsChanged -= _forwardSessionsChanged;
		_system.Dispose();
		foreach (var source in _sources)
		{
			source.SessionsChanged -= _forwardSessionsChanged;
			source.Dispose();
		}
	}

	private IMediaPlatform For(string appId)
	{
		foreach (var source in _sources)
		{
			if (source.Owns(appId))
			{
				return source;
			}
		}

		return _system;
	}
}
