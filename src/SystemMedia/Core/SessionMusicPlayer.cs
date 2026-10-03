using System.Security.Cryptography;
using MacroDeck.Sdk.MusicPlayer;
using SystemMedia.Platform;

namespace SystemMedia.Core;

internal sealed class SessionMusicPlayer(
	IMediaPlatform platform,
	CurrentAppSelection current,
	string? appId,
	TimeProvider time,
	YouTubePageCorrector? youTube = null,
	ThumbnailHistory? thumbnails = null,
	TimeSpan cycleInterval = default) : IMusicPlayer, IDisposable
{
	private readonly ThumbnailHistory _thumbnails = thumbnails ?? ThumbnailHistory.InMemory();

	private readonly SemaphoreSlim _filterGate = new(1, 1);

	// Per app, so the any-app player switching between apps does not make each forget what it has seen.
	private readonly Dictionary<string, AppFilters> _filters = new(StringComparer.Ordinal);

	private volatile CachedArtwork? _artwork;

	public async Task<MusicPlayerState> GetStateAsync(CancellationToken cancellationToken = default)
	{
		var (state, shown) = await ReadAsync(cancellationToken);
		return cycleInterval > TimeSpan.Zero && shown is not null && current.CyclePosition(shown) is { } position
			? state with { Badge = $"{position.Number}/{position.Count}" }
			: state;
	}

	private async Task<(MusicPlayerState State, string? Shown)> ReadAsync(CancellationToken cancellationToken)
	{
		if (!platform.IsStarted)
		{
			return (MusicPlayerState.Unavailable("Media controls unavailable"), null);
		}

		if (Resolve() is not { } resolved)
		{
			return (MediaStateMapper.Idle(PinnedName()), null);
		}

		var app = platform.ResolveApp(resolved);
		var snapshot = await ReadFilteredAsync(resolved, app, cancellationToken);
		if (snapshot is null)
		{
			return (MediaStateMapper.Idle(PinnedName()), null);
		}

		var state = MediaStateMapper.ToState(snapshot, time.GetUtcNow(), platform.GetVolumePercent(app));
		if (state.ArtworkId is null && platform.HasAppIcon(app))
		{
			state = state with { ArtworkId = AppIconArtwork.IdFor(app.AppId) };
		}

		return (state, resolved);
	}

	public async Task<MusicPlayerArtwork?> GetArtworkAsync(string artworkId, CancellationToken cancellationToken = default)
	{
		if (_artwork is { } cached && string.Equals(cached.Id, artworkId, StringComparison.Ordinal))
		{
			return cached.Artwork;
		}

		if (Resolve() is not { } resolved)
		{
			return null;
		}

		if (AppIconArtwork.IsIconId(artworkId))
		{
			return string.Equals(AppIconArtwork.IdFor(resolved), artworkId, StringComparison.Ordinal)
				? await platform.GetAppIconAsync(platform.ResolveApp(resolved), cancellationToken)
				: null;
		}

		// Only the track the id was issued for may answer it; the session may have moved on since.
		if (await platform.ReadAsync(resolved, cancellationToken) is not { } reported ||
			await CorrectAsync(reported, cancellationToken) is not { HasThumbnail: true } snapshot ||
			!string.Equals(MediaStateMapper.ArtworkId(snapshot), artworkId, StringComparison.Ordinal))
		{
			return null;
		}

		var artwork = youTube?.CorrectedThumbnail(resolved) ?? await platform.ReadThumbnailAsync(resolved, cancellationToken);
		if (artwork is not null)
		{
			_artwork = new CachedArtwork(artworkId, artwork);
		}

		return artwork;
	}

	public void Dispose() => _filterGate.Dispose();

	public Task PlayAsync(CancellationToken cancellationToken = default) =>
		RunAsync((id, ct) => platform.SendAsync(id, MediaCommand.Play, ct), "play", cancellationToken);

	public Task PauseAsync(CancellationToken cancellationToken = default) =>
		RunAsync((id, ct) => platform.SendAsync(id, MediaCommand.Pause, ct), "pause", cancellationToken);

	public Task TogglePlayPauseAsync(CancellationToken cancellationToken = default) =>
		RunAsync((id, ct) => platform.SendAsync(id, MediaCommand.TogglePlayPause, ct), "toggle play/pause", cancellationToken);

	public Task NextAsync(CancellationToken cancellationToken = default) =>
		RunAsync((id, ct) => platform.SendAsync(id, MediaCommand.Next, ct), "next", cancellationToken);

	public Task PreviousAsync(CancellationToken cancellationToken = default) =>
		RunAsync((id, ct) => platform.SendAsync(id, MediaCommand.Previous, ct), "previous", cancellationToken);

	public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default) =>
		RunAsync((id, ct) => platform.SeekAsync(id, position < TimeSpan.Zero ? TimeSpan.Zero : position, ct), "seek", cancellationToken);

	public async Task SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default)
	{
		var app = platform.ResolveApp(RequireApp());
		if (!await platform.SetVolumePercentAsync(app, volumePercent, cancellationToken))
		{
			throw new MediaCommandRejectedException(app.DisplayName, "volume");
		}
	}

	public Task SetShuffleAsync(bool enabled, CancellationToken cancellationToken = default) =>
		RunAsync((id, ct) => platform.SetShuffleAsync(id, enabled, ct), "shuffle", cancellationToken);

	public Task SetRepeatModeAsync(RepeatMode mode, CancellationToken cancellationToken = default) =>
		RunAsync((id, ct) => platform.SetRepeatModeAsync(id, mode, ct), "repeat", cancellationToken);

	private string? Resolve() => appId ?? current.Current(cycleInterval);

	private string? PinnedName() => appId is null ? null : platform.ResolveApp(appId).DisplayName;

	private string RequireApp() => Resolve() ?? throw new MediaSessionNotFoundException(PinnedName());

	// The YouTube correction runs last so the leftover filter always compares what the app itself reported.
	private async Task<MediaSnapshot?> ReadFilteredAsync(string resolved, AppIdentity app, CancellationToken cancellationToken)
	{
		var snapshot = await platform.ReadAsync(resolved, cancellationToken);
		if (snapshot is null)
		{
			return null;
		}

		snapshot = snapshot with { AppName = app.DisplayName };
		var now = time.GetUtcNow();

		await _filterGate.WaitAsync(cancellationToken);
		try
		{
			if (!_filters.TryGetValue(resolved, out var filters))
			{
				filters = _filters[resolved] = new AppFilters(new StaleMediaFilter(_thumbnails), new DroppedTimelineRestorer());
			}

			string? thumbnailHash = null;
			if (filters.Leftovers.NeedsThumbnailHash(snapshot, now) &&
				await platform.ReadThumbnailAsync(resolved, cancellationToken) is { } artwork)
			{
				thumbnailHash = Convert.ToHexStringLower(SHA256.HashData(artwork.Data));
				_artwork = new CachedArtwork(MediaStateMapper.ArtworkId(snapshot), artwork);
			}

			snapshot = filters.Timelines.Apply(filters.Leftovers.Apply(snapshot, thumbnailHash, now));
		}
		finally
		{
			_filterGate.Release();
		}

		return await CorrectAsync(snapshot, cancellationToken);
	}

	private async Task<MediaSnapshot> CorrectAsync(MediaSnapshot snapshot, CancellationToken cancellationToken) =>
		youTube is { Enabled: true }
			? youTube.Correct(snapshot, await platform.ReadBrowserTabsAsync(snapshot.AppId, cancellationToken))
			: snapshot;

	private async Task RunAsync(Func<string, CancellationToken, Task<bool>> command, string commandName, CancellationToken cancellationToken)
	{
		var resolved = RequireApp();
		if (!await command(resolved, cancellationToken))
		{
			throw new MediaCommandRejectedException(platform.ResolveApp(resolved).DisplayName, commandName);
		}
	}

	private sealed record CachedArtwork(string Id, MusicPlayerArtwork Artwork);

	private sealed record AppFilters(StaleMediaFilter Leftovers, DroppedTimelineRestorer Timelines);
}
