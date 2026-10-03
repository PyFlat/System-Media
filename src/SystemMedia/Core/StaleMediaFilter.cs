namespace SystemMedia.Core;

// A browser keeps one session for all its tabs, so a page that reports only a title inherits the previous
// page's timeline and thumbnail. A thumbnail is stale while it was first seen with a track from another album
// or artist.
internal sealed class StaleMediaFilter(ThumbnailHistory history)
{
	internal static readonly TimeSpan StaleThumbnailRecheck = TimeSpan.FromSeconds(3);

	private static readonly DateTimeOffset _earliestRealStamp = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

	private Track? _track;
	private DateTimeOffset _timelineStamp;
	private string? _timelineStampTrack;

	internal bool NeedsThumbnailHash(MediaSnapshot snapshot, DateTimeOffset now) =>
		snapshot.HasThumbnail &&
		(_track is not { } track ||
			!string.Equals(track.Key, snapshot.TrackKey, StringComparison.Ordinal) ||
			(track.ArtworkStale && now - track.LastThumbnailCheck >= StaleThumbnailRecheck));

	internal MediaSnapshot Apply(MediaSnapshot snapshot, string? thumbnailHash, DateTimeOffset now)
	{
		var key = snapshot.TrackKey;
		if (_timelineStampTrack is null || snapshot.LastUpdatedTime != _timelineStamp)
		{
			_timelineStamp = snapshot.LastUpdatedTime;
			_timelineStampTrack = key;
		}

		if (_track is null || !string.Equals(_track.Key, key, StringComparison.Ordinal))
		{
			_track = new Track(key) { LastThumbnailCheck = now, ArtworkStale = IsLeftover(snapshot, thumbnailHash) };
		}
		else if (_track.ArtworkStale && thumbnailHash is not null)
		{
			_track.LastThumbnailCheck = now;
			_track.ArtworkStale = IsLeftover(snapshot, thumbnailHash);
		}

		var filtered = snapshot;
		if (HasTimeline(snapshot) &&
			snapshot.LastUpdatedTime > _earliestRealStamp &&
			!string.Equals(_timelineStampTrack, key, StringComparison.Ordinal))
		{
			filtered = filtered with { StartTime = TimeSpan.Zero, EndTime = TimeSpan.Zero, Position = TimeSpan.Zero };
		}

		if (_track.ArtworkStale)
		{
			filtered = filtered with { HasThumbnail = false };
		}

		return filtered;
	}

	private bool IsLeftover(MediaSnapshot snapshot, string? thumbnailHash)
	{
		if (thumbnailHash is null)
		{
			return false;
		}

		var first = history.FirstSeen(snapshot.AppId, thumbnailHash, snapshot);
		var now = ThumbnailSighting.Of(snapshot);
		return !string.Equals(first.Track, now.Track, StringComparison.Ordinal) && !first.SharesAlbumOrArtist(now);
	}

	private static bool HasTimeline(MediaSnapshot snapshot) =>
		snapshot.HasDuration || snapshot.Position > TimeSpan.Zero;

	private sealed class Track(string key)
	{
		public string Key { get; } = key;

		public DateTimeOffset LastThumbnailCheck { get; set; }

		public bool ArtworkStale { get; set; }
	}
}
