using MacroDeck.Sdk.MusicPlayer;
using NUnit.Framework;
using SystemMedia.Core;
using SystemMedia.Platform;

namespace SystemMedia.Tests;

[TestFixture]
public sealed class SessionMusicPlayerTests
{
	private const string Spotify = "spotify";
	private const string Browser = "browser";

	[Test]
	public async Task While_cycling_the_any_app_player_shows_its_place_as_a_badge()
	{
		var platform = new FakeMediaPlatform { SystemCurrent = Browser };
		platform.Sessions[Spotify] = FakeMediaPlatform.Playing(Spotify, "Numb") with { Artist = "Linkin Park" };
		platform.Sessions[Browser] = FakeMediaPlatform.Playing(Browser, "Video");
		var selection = new CurrentAppSelection(platform, TimeProvider.System);
		using var player = new SessionMusicPlayer(platform, selection, appId: null, TimeProvider.System, cycleInterval: TimeSpan.FromMinutes(1));

		Assert.That((await player.GetStateAsync()).Badge, Is.EqualTo("2/2"));
		Assert.That(selection.ShowNextPlaying(), Is.True);
		var state = await player.GetStateAsync();
		Assert.That(state.Badge, Is.EqualTo("1/2"));
		Assert.That(state.Artists.Single(), Is.EqualTo("Linkin Park"));
	}

	[Test]
	public async Task Without_cycling_or_a_second_playing_app_there_is_no_badge()
	{
		var platform = new FakeMediaPlatform { SystemCurrent = Spotify };
		platform.Sessions[Spotify] = FakeMediaPlatform.Playing(Spotify, "Numb") with { Artist = "Linkin Park" };
		platform.Sessions[Browser] = FakeMediaPlatform.Playing(Browser, "Video") with { Status = PlaybackState.Paused };
		var selection = new CurrentAppSelection(platform, TimeProvider.System);
		using var cycling = new SessionMusicPlayer(platform, selection, appId: null, TimeProvider.System, cycleInterval: TimeSpan.FromMinutes(1));
		using var plain = new SessionMusicPlayer(platform, selection, appId: null, TimeProvider.System);
		using var pinned = new SessionMusicPlayer(platform, selection, Spotify, TimeProvider.System);

		Assert.That((await cycling.GetStateAsync()).Badge, Is.Null);

		platform.Sessions[Browser] = platform.Sessions[Browser] with { Status = PlaybackState.Playing };
		Assert.That((await pinned.GetStateAsync()).Badge, Is.Null);
		Assert.That((await plain.GetStateAsync()).Badge, Is.Null);
	}

	[Test]
	public async Task The_any_app_player_still_hides_a_leftover_cover_after_showing_another_app()
	{
		const string Vlc = "vlc.exe";
		var platform = new FakeMediaPlatform { SystemCurrent = Browser };
		platform.Sessions[Browser] = FakeMediaPlatform.Playing(Browser, "Night Transit") with { Artist = "The Artist" };
		platform.Sessions[Vlc] = FakeMediaPlatform.Playing(Vlc, "Movie");
		var selection = new CurrentAppSelection(platform, TimeProvider.System);
		using var player = new SessionMusicPlayer(platform, selection, appId: null, TimeProvider.System);

		Assert.That((await player.GetStateAsync()).ArtworkId, Is.Not.Null);
		Assert.That(selection.ShowNextPlaying(), Is.True);
		Assert.That((await player.GetStateAsync()).TrackName, Is.EqualTo("Movie"));

		// The fake hands every track of one app the same thumbnail, like a browser keeping an old cover.
		platform.Sessions[Browser] = FakeMediaPlatform.Playing(Browser, "TikTok - Make Your Day");
		Assert.That(selection.ShowNextPlaying(), Is.True);
		var state = await player.GetStateAsync();

		Assert.That(state.TrackName, Is.EqualTo("TikTok - Make Your Day"));
		Assert.That(state.ArtworkId, Is.EqualTo(AppIconArtwork.IdFor(Browser)), "the app icon, not the old cover");
	}

	[Test]
	public async Task The_any_app_player_shows_the_app_that_is_playing()
	{
		var platform = new FakeMediaPlatform { SystemCurrent = Spotify };
		platform.Sessions[Spotify] = FakeMediaPlatform.Playing(Spotify, "Numb");
		using var player = Create(platform, appId: null);

		var state = await player.GetStateAsync();

		Assert.That(state.TrackName, Is.EqualTo("Numb"));
		Assert.That(state.DeviceName, Is.EqualTo("App spotify"));
	}

	[Test]
	public async Task A_pinned_player_stays_on_its_app_while_another_one_is_current()
	{
		var platform = new FakeMediaPlatform { SystemCurrent = Browser };
		platform.Sessions[Spotify] = FakeMediaPlatform.Playing(Spotify, "Numb");
		platform.Sessions[Browser] = FakeMediaPlatform.Playing(Browser, "Video");
		using var player = Create(platform, Spotify);

		Assert.That((await player.GetStateAsync()).TrackName, Is.EqualTo("Numb"));
	}

	[Test]
	public async Task A_pinned_app_that_is_not_playing_is_idle_under_its_own_name()
	{
		using var player = Create(new FakeMediaPlatform(), Spotify);

		var state = await player.GetStateAsync();

		Assert.That(state.IsConnected, Is.True);
		Assert.That(state.PlaybackState, Is.EqualTo(PlaybackState.Stopped));
		Assert.That(state.DeviceName, Is.EqualTo("App spotify"));
	}

	[Test]
	public async Task A_platform_that_could_not_start_reports_unavailable_not_idle()
	{
		using var player = Create(new FakeMediaPlatform { IsStarted = false }, appId: null);

		Assert.That((await player.GetStateAsync()).IsUnavailable, Is.True);
	}

	[Test]
	public async Task A_track_without_a_cover_shows_the_app_icon_instead()
	{
		var platform = new FakeMediaPlatform { SystemCurrent = Browser };
		platform.Sessions[Browser] = FakeMediaPlatform.Playing(Browser, "TikTok", hasThumbnail: false);
		using var player = Create(platform, appId: null);

		var state = await player.GetStateAsync();
		var artwork = await player.GetArtworkAsync(state.ArtworkId!);

		Assert.That(AppIconArtwork.IsIconId(state.ArtworkId!), Is.True);
		Assert.That(artwork!.Data, Is.EqualTo(new byte[] { 1, 2, 3 }));
	}

	[Test]
	public async Task A_track_s_cover_is_served_for_its_artwork_id()
	{
		var platform = new FakeMediaPlatform { SystemCurrent = Spotify };
		platform.Sessions[Spotify] = FakeMediaPlatform.Playing(Spotify, "Numb");
		using var player = Create(platform, appId: null);

		var state = await player.GetStateAsync();

		Assert.That(await player.GetArtworkAsync(state.ArtworkId!), Is.Not.Null);
		Assert.That(await player.GetArtworkAsync("art-0000000000000000"), Is.Null);
	}

	[Test]
	public async Task Commands_go_to_the_resolved_app()
	{
		var platform = new FakeMediaPlatform { SystemCurrent = Spotify };
		platform.Sessions[Spotify] = FakeMediaPlatform.Playing(Spotify, "Numb");
		using var player = Create(platform, appId: null);

		await player.NextAsync();

		Assert.That(platform.Sent, Is.EqualTo(new[] { (Spotify, MediaCommand.Next) }));
	}

	[Test]
	public async Task A_command_the_app_declines_fails_instead_of_looking_obeyed()
	{
		var platform = new FakeMediaPlatform { SystemCurrent = Spotify, AcceptCommands = false };
		platform.Sessions[Spotify] = FakeMediaPlatform.Playing(Spotify, "Numb");
		using var player = Create(platform, appId: null);

		await Assert.ThrowsAsync<MediaCommandRejectedException>(() => player.NextAsync());
	}

	[Test]
	public async Task A_command_with_nothing_playing_fails()
	{
		using var player = Create(new FakeMediaPlatform(), appId: null);

		await Assert.ThrowsAsync<MediaSessionNotFoundException>(() => player.PlayAsync());
	}

	[Test]
	public async Task A_platform_without_per_app_volume_reports_none_so_the_widget_hides_the_slider()
	{
		var platform = new FakeMediaPlatform { SystemCurrent = Spotify, SupportsVolume = false };
		platform.Sessions[Spotify] = FakeMediaPlatform.Playing(Spotify, "Numb");
		using var player = Create(platform, appId: null);

		Assert.That((await player.GetStateAsync()).VolumePercent, Is.Null);
		await Assert.ThrowsAsync<MediaCommandRejectedException>(() => player.SetVolumeAsync(50));
	}

	private static SessionMusicPlayer Create(FakeMediaPlatform platform, string? appId) =>
		new(platform, new CurrentAppSelection(platform, TimeProvider.System), appId, TimeProvider.System);
}
