using MacroDeck.Sdk.MusicPlayer;

namespace SystemMedia.Events;

internal sealed record AppObservation(string InstanceId, string AppName, MusicPlayerState State);

internal sealed record MediaEvent(string EventId, IReadOnlyDictionary<string, object?> Payload);

// The first observation of an app is a baseline: starting while Spotify plays is not a track change.
internal sealed class MediaChangeDetector
{
	private readonly Dictionary<string, (string? Track, PlaybackState State)> _apps = new(StringComparer.Ordinal);
	private bool _currentSeen;
	private string? _currentApp;
	private string? _currentAppName;

	internal IReadOnlyList<MediaEvent> Observe(IReadOnlyList<AppObservation> observations, string? currentInstanceId)
	{
		var events = new List<MediaEvent>();
		foreach (var observation in observations)
		{
			var track = TrackKey(observation.State);
			var state = observation.State.PlaybackState;
			if (_apps.TryGetValue(observation.InstanceId, out var previous))
			{
				if (track is not null && !string.Equals(track, previous.Track, StringComparison.Ordinal))
				{
					events.Add(new MediaEvent(MediaEvents.TrackChangedId, MediaEvents.TrackChangedPayload(observation)));
				}

				if (state != previous.State)
				{
					events.Add(new MediaEvent(MediaEvents.PlaybackStateChangedId, MediaEvents.PlaybackStatePayload(observation)));
				}
			}

			_apps[observation.InstanceId] = (track, state);
		}

		var current = currentInstanceId is null ? null : observations.FirstOrDefault(o => string.Equals(o.InstanceId, currentInstanceId, StringComparison.Ordinal));
		if (_currentSeen && current is not null && !string.Equals(current.InstanceId, _currentApp, StringComparison.Ordinal))
		{
			events.Add(new MediaEvent(MediaEvents.CurrentAppChangedId, MediaEvents.CurrentAppPayload(current, _currentAppName)));
		}

		if (current is not null || !_currentSeen)
		{
			_currentSeen = true;
			_currentApp = current?.InstanceId;
			_currentAppName = current?.AppName;
		}

		return events;
	}

	private static string? TrackKey(MusicPlayerState state) =>
		state.TrackName is { Length: > 0 } title
			? string.Join('\u001f', title, string.Join(", ", state.Artists), state.AlbumName)
			: null;
}
