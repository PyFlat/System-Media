using MacroDeck.Sdk.MusicPlayer;

namespace SystemMedia.Core;

// Firefox clears a page's timeline on every play, pause and seek once a second video on it has played with
// sound. A play or pause does not move the track, so the last timeline still holds; after a seek it does not.
internal sealed class DroppedTimelineRestorer
{
	private string? _track;
	private Timeline? _last;

	internal MediaSnapshot Apply(MediaSnapshot snapshot)
	{
		var key = snapshot.TrackKey;
		if (!string.Equals(_track, key, StringComparison.Ordinal))
		{
			_track = key;
			_last = null;
		}

		if (snapshot.HasDuration)
		{
			// Only a new stamp is a new report; the status can change a moment before the timeline does.
			if (_last is not { } known || known.Stamp != snapshot.LastUpdatedTime)
			{
				_last = new Timeline(snapshot.StartTime, snapshot.EndTime, snapshot.Position, snapshot.LastUpdatedTime, snapshot.Status, Rate(snapshot));
			}

			return snapshot;
		}

		if (_last is not { } last)
		{
			return snapshot;
		}

		if (snapshot.LastUpdatedTime != last.Stamp)
		{
			if (snapshot.Status == last.Status)
			{
				_last = null;
				return snapshot;
			}

			_last = last = last with
			{
				Position = last.PositionAt(snapshot.LastUpdatedTime),
				Stamp = snapshot.LastUpdatedTime,
				Status = snapshot.Status,
			};
		}

		return snapshot with { StartTime = last.Start, EndTime = last.End, Position = last.Position };
	}

	private static double Rate(MediaSnapshot snapshot) =>
		snapshot.PlaybackRate is { } rate && rate > 0 ? rate : 1d;

	private sealed record Timeline(
		TimeSpan Start,
		TimeSpan End,
		TimeSpan Position,
		DateTimeOffset Stamp,
		PlaybackState Status,
		double Rate)
	{
		public TimeSpan PositionAt(DateTimeOffset time)
		{
			var position = Status == PlaybackState.Playing && time > Stamp
				? Position + (time - Stamp) * Rate
				: Position;
			return position > End ? End : position;
		}
	}
}
