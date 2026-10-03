using MacroDeck.Sdk.MusicPlayer;
using NUnit.Framework;
using SystemMedia.Events;

namespace SystemMedia.Tests;

[TestFixture]
public sealed class MediaChangeDetectorTests
{
	private const string Spotify = "app-spotify";
	private const string Firefox = "app-firefox";

	private static readonly string[] _stateChangedOnly = [MediaEvents.PlaybackStateChangedId];

	[Test]
	public void The_first_round_is_only_a_baseline()
	{
		var detector = new MediaChangeDetector();

		var events = detector.Observe([Playing(Spotify, "Song A")], Spotify);

		Assert.That(events, Is.Empty);
	}

	[Test]
	public void A_new_track_fires_track_changed_with_its_metadata()
	{
		var detector = new MediaChangeDetector();
		detector.Observe([Playing(Spotify, "Song A")], Spotify);

		var events = detector.Observe([Playing(Spotify, "Song B")], Spotify);

		var trackChanged = events.Single();
		Assert.That(trackChanged.EventId, Is.EqualTo(MediaEvents.TrackChangedId));
		Assert.That(trackChanged.Payload[MediaEvents.AppParameter], Is.EqualTo(Spotify));
		Assert.That(trackChanged.Payload[MediaEvents.TrackParameter], Is.EqualTo("Song B"));
		Assert.That(trackChanged.Payload[MediaEvents.ArtistParameter], Is.EqualTo("Artist"));
	}

	[Test]
	public void An_unchanged_track_fires_nothing()
	{
		var detector = new MediaChangeDetector();
		detector.Observe([Playing(Spotify, "Song A")], Spotify);

		Assert.That(detector.Observe([Playing(Spotify, "Song A")], Spotify), Is.Empty);
	}

	[Test]
	public void Pausing_fires_playback_state_changed_with_the_new_state()
	{
		var detector = new MediaChangeDetector();
		detector.Observe([Playing(Spotify, "Song A")], Spotify);

		var events = detector.Observe([Playing(Spotify, "Song A") with { State = Playing(Spotify, "Song A").State with { PlaybackState = PlaybackState.Paused } }], Spotify);

		var stateChanged = events.Single();
		Assert.That(stateChanged.EventId, Is.EqualTo(MediaEvents.PlaybackStateChangedId));
		Assert.That(stateChanged.Payload[MediaEvents.StateParameter], Is.EqualTo("paused"));
	}

	[Test]
	public void An_app_closing_its_session_is_a_stop_not_a_track_change()
	{
		var detector = new MediaChangeDetector();
		detector.Observe([Playing(Spotify, "Song A")], Spotify);

		var closed = new AppObservation(Spotify, "Spotify", new MusicPlayerState { IsConnected = true, PlaybackState = PlaybackState.Stopped });
		var events = detector.Observe([closed], currentInstanceId: null);

		Assert.That(events.Select(e => e.EventId), Is.EqualTo(_stateChangedOnly));
	}

	[Test]
	public void Windows_switching_to_another_app_fires_current_app_changed()
	{
		var detector = new MediaChangeDetector();
		detector.Observe([Playing(Spotify, "Song A"), Playing(Firefox, "Video")], Spotify);

		var events = detector.Observe([Playing(Spotify, "Song A"), Playing(Firefox, "Video")], Firefox);

		var appChanged = events.Single();
		Assert.That(appChanged.EventId, Is.EqualTo(MediaEvents.CurrentAppChangedId));
		Assert.That(appChanged.Payload[MediaEvents.AppParameter], Is.EqualTo(Firefox));
		Assert.That(appChanged.Payload[MediaEvents.PreviousAppNameParameter], Is.EqualTo("Spotify"));
	}

	[Test]
	public void No_current_app_for_a_moment_does_not_count_as_a_switch()
	{
		var detector = new MediaChangeDetector();
		detector.Observe([Playing(Spotify, "Song A")], Spotify);
		detector.Observe([Playing(Spotify, "Song A")], currentInstanceId: null);

		Assert.That(detector.Observe([Playing(Spotify, "Song A")], Spotify), Is.Empty);
	}

	private static AppObservation Playing(string instanceId, string track) => new(
		instanceId,
		instanceId == Spotify ? "Spotify" : "Firefox",
		new MusicPlayerState { IsConnected = true, PlaybackState = PlaybackState.Playing, TrackName = track, Artists = ["Artist"] });
}
