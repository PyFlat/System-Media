using MacroDeck.Sdk.MusicPlayer;
using NUnit.Framework;
using SystemMedia.Platform.MacOS;

namespace SystemMedia.Tests;

[TestFixture]
public sealed class NowPlayingStateTests
{
	private const string Full =
		"""{"type":"data","diff":false,"payload":{"bundleIdentifier":"com.spotify.client","playing":true,"title":"Numb","artist":"Linkin Park","album":"Meteora","durationMicros":187000000,"elapsedTimeMicros":30000000,"timestampEpochMicros":1790000000000000,"playbackRate":1,"shuffleMode":1,"repeatMode":3,"artworkMimeType":"image/png","artworkData":"iVBORw0KGgo="}}""";

	[Test]
	public void A_full_payload_describes_what_is_now_playing()
	{
		var state = new NowPlayingState();

		var appChanged = state.Apply(Full);

		var current = state.Current!;
		Assert.That(appChanged, Is.True);
		Assert.That(current.AppId, Is.EqualTo("com.spotify.client"));
		Assert.That(current.Playing, Is.True);
		Assert.That(current.Title, Is.EqualTo("Numb"));
		Assert.That(current.DurationMicros, Is.EqualTo(187_000_000));
	}

	[Test]
	public void A_diff_updates_only_the_keys_it_carries()
	{
		var state = new NowPlayingState();
		state.Apply(Full);

		var appChanged = state.Apply("""{"type":"data","diff":true,"payload":{"playing":false,"elapsedTimeMicros":31000000}}""");

		Assert.That(appChanged, Is.False);
		Assert.That(state.Current!.Playing, Is.False);
		Assert.That(state.Current.ElapsedMicros, Is.EqualTo(31_000_000));
		Assert.That(state.Current.Title, Is.EqualTo("Numb"));
	}

	[Test]
	public void A_key_set_to_null_in_a_diff_has_vanished()
	{
		var state = new NowPlayingState();
		state.Apply(Full);

		state.Apply("""{"type":"data","diff":true,"payload":{"artworkData":null,"artworkMimeType":null}}""");

		Assert.That(state.Current!.ArtworkBase64, Is.Null);
		Assert.That(state.Current.ToSnapshot().HasThumbnail, Is.False);
	}

	[Test]
	public void An_empty_full_payload_means_nothing_is_playing()
	{
		var state = new NowPlayingState();
		state.Apply(Full);

		var appChanged = state.Apply("""{"type":"data","diff":false,"payload":{}}""");

		Assert.That(appChanged, Is.True);
		Assert.That(state.Current, Is.Null);
	}

	[Test]
	public void Browser_media_is_attributed_to_the_browser_not_its_helper_process()
	{
		var state = new NowPlayingState();

		state.Apply("""{"type":"data","diff":false,"payload":{"bundleIdentifier":"com.apple.WebKit.GPU","parentApplicationBundleIdentifier":"com.apple.Safari","playing":true,"title":"Video"}}""");

		Assert.That(state.Current!.AppId, Is.EqualTo("com.apple.Safari"));
	}

	[Test]
	public void The_snapshot_carries_the_timeline_modes_and_artwork()
	{
		var state = new NowPlayingState();
		state.Apply(Full);

		var snapshot = state.Current!.ToSnapshot();
		var artwork = state.Current.DecodeArtwork();

		Assert.That(snapshot.Status, Is.EqualTo(PlaybackState.Playing));
		Assert.That(snapshot.EndTime, Is.EqualTo(TimeSpan.FromSeconds(187)));
		Assert.That(snapshot.Position, Is.EqualTo(TimeSpan.FromSeconds(30)));
		Assert.That(snapshot.LastUpdatedTime, Is.EqualTo(DateTimeOffset.FromUnixTimeSeconds(1_790_000_000)));
		Assert.That(snapshot.IsShuffleActive, Is.False);
		Assert.That(snapshot.RepeatMode, Is.EqualTo(RepeatMode.Context));
		Assert.That(artwork!.MimeType, Is.EqualTo("image/png"));
	}

	[Test]
	public void Lines_that_are_not_data_are_ignored()
	{
		var state = new NowPlayingState();

		Assert.That(state.Apply("""{"type":"something-else"}"""), Is.False);
		Assert.That(state.Current, Is.Null);
	}
}
