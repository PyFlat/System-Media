using MacroDeck.Sdk.MusicPlayer;
using NUnit.Framework;
using SystemMedia.Platform.Linux;

namespace SystemMedia.Tests.Linux;

[TestFixture]
public sealed class MprisPlayerTests
{
	private static readonly DateTimeOffset _readAt = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

	[TestCase("org.mpris.MediaPlayer2.spotify", "spotify")]
	[TestCase("org.mpris.MediaPlayer2.firefox.instance_1_23", "firefox")]
	[TestCase("org.mpris.MediaPlayer2.chromium.instance4711", "chromium")]
	[TestCase("org.mpris.MediaPlayer2.io.github.celluloid_player.Celluloid", "io.github.celluloid_player.Celluloid")]
	public void Every_instance_of_a_player_belongs_to_one_app(string busName, string appId) =>
		Assert.That(MprisPlayer.AppIdOf(busName), Is.EqualTo(appId));

	[Test]
	public void An_instance_launched_as_a_variant_is_an_app_of_its_own()
	{
		var firefox = new MprisPlayer("org.mpris.MediaPlayer2.firefox.instance_1_918", "Mozilla Firefox", "firefox", new Dictionary<string, object?>());

		var webApp = firefox.LaunchedAs("WebApp-SpotifyWeb3778", "Spotify");
		var profile = firefox.LaunchedAs("work", desktopName: null);

		Assert.That(firefox.IsInstance, Is.True);
		Assert.That(firefox.AppId, Is.EqualTo("firefox"));
		Assert.That(webApp.AppId, Is.EqualTo("firefox@WebApp-SpotifyWeb3778"));
		Assert.That(webApp.Identity, Is.EqualTo("Spotify"));
		Assert.That(webApp.DesktopEntry, Is.EqualTo("WebApp-SpotifyWeb3778"));
		Assert.That(profile.AppId, Is.EqualTo("firefox@work"));
		Assert.That(profile.Identity, Is.EqualTo("Mozilla Firefox (work)"));
		Assert.That(profile.DesktopEntry, Is.EqualTo("firefox"));
		Assert.That(Player(new()).IsInstance, Is.False);
	}

	[Test]
	public void A_playing_track_becomes_a_snapshot_with_its_timeline()
	{
		var snapshot = Player(new()
		{
			["PlaybackStatus"] = "Playing",
			["Position"] = 30_000_000L,
			["Rate"] = 1d,
			["Shuffle"] = true,
			["LoopStatus"] = "Playlist",
			["Metadata"] = new Dictionary<string, object?>
			{
				["xesam:title"] = "Numb",
				["xesam:artist"] = new object?[] { "Linkin Park" },
				["xesam:album"] = "Meteora",
				["mpris:length"] = 185_000_000L,
				["mpris:artUrl"] = "https://i.scdn.co/image/cover",
			},
		}).ToSnapshot(_readAt);

		Assert.That(snapshot.AppId, Is.EqualTo("spotify"));
		Assert.That(snapshot.AppName, Is.EqualTo("Spotify"));
		Assert.That(snapshot.Title, Is.EqualTo("Numb"));
		Assert.That(snapshot.Artist, Is.EqualTo("Linkin Park"));
		Assert.That(snapshot.AlbumTitle, Is.EqualTo("Meteora"));
		Assert.That(snapshot.Status, Is.EqualTo(PlaybackState.Playing));
		Assert.That(snapshot.EndTime, Is.EqualTo(TimeSpan.FromSeconds(185)));
		Assert.That(snapshot.Position, Is.EqualTo(TimeSpan.FromSeconds(30)));
		Assert.That(snapshot.LastUpdatedTime, Is.EqualTo(_readAt));
		Assert.That(snapshot.HasThumbnail, Is.True);
		Assert.That(snapshot.IsShuffleActive, Is.True);
		Assert.That(snapshot.RepeatMode, Is.EqualTo(RepeatMode.Context));
	}

	[Test]
	public void Several_artists_are_joined_and_a_single_string_is_accepted_too()
	{
		var listed = Player(Metadata("xesam:artist", new object?[] { "A", "", "B" })).ToSnapshot(_readAt);
		var single = Player(Metadata("xesam:artist", "Solo")).ToSnapshot(_readAt);

		Assert.That(listed.Artist, Is.EqualTo("A, B"));
		Assert.That(single.Artist, Is.EqualTo("Solo"));
	}

	[Test]
	public void A_player_without_a_timeline_or_cover_reports_none()
	{
		var snapshot = Player(new() { ["PlaybackStatus"] = "Stopped" }).ToSnapshot(_readAt);

		Assert.That(snapshot.Status, Is.EqualTo(PlaybackState.Stopped));
		Assert.That(snapshot.HasDuration, Is.False);
		Assert.That(snapshot.HasThumbnail, Is.False);
		Assert.That(snapshot.IsShuffleActive, Is.Null);
		Assert.That(snapshot.RepeatMode, Is.Null);
		Assert.That(snapshot.Title, Is.Null);
	}

	[Test]
	public void A_length_sent_as_a_double_still_counts()
	{
		var snapshot = Player(Metadata("mpris:length", 60_000_000d)).ToSnapshot(_readAt);

		Assert.That(snapshot.EndTime, Is.EqualTo(TimeSpan.FromMinutes(1)));
	}

	[Test]
	public void A_length_too_long_for_a_timespan_counts_as_none()
	{
		var snapshot = Player(new()
		{
			["Metadata"] = new Dictionary<string, object?> { ["mpris:length"] = long.MaxValue },
			["Position"] = long.MaxValue,
		}).ToSnapshot(_readAt);

		Assert.That(snapshot.HasDuration, Is.False);
		Assert.That(snapshot.Position, Is.EqualTo(TimeSpan.Zero));
	}

	[Test]
	public void The_no_track_id_is_not_a_track_to_seek_in()
	{
		Assert.That(Player(Metadata("mpris:trackid", "/org/mpris/MediaPlayer2/TrackList/NoTrack")).TrackId, Is.Null);
		Assert.That(Player(Metadata("mpris:trackid", "/com/spotify/track/1")).TrackId, Is.EqualTo("/com/spotify/track/1"));
	}

	[Test]
	public void A_player_that_cannot_be_controlled_refuses_everything()
	{
		var locked = Player(new() { ["CanControl"] = false, ["CanGoNext"] = true });
		var open = Player(new() { ["CanGoNext"] = false });
		var silent = Player(new());

		Assert.That(locked.Can("CanGoNext"), Is.False);
		Assert.That(open.Can("CanGoNext"), Is.False);
		Assert.That(open.Can("CanPause"), Is.True, "an unreported capability is assumed");
		Assert.That(silent.Can("CanSeek"), Is.True);
	}

	private static Dictionary<string, object?> Metadata(string key, object? value) =>
		new() { ["PlaybackStatus"] = "Paused", ["Metadata"] = new Dictionary<string, object?> { [key] = value } };

	private static MprisPlayer Player(Dictionary<string, object?> properties) =>
		new("org.mpris.MediaPlayer2.spotify", "Spotify", "spotify", properties);
}
