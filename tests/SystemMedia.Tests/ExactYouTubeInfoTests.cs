using MacroDeck.Sdk.MusicPlayer;
using NUnit.Framework;
using Serilog;
using SystemMedia.Core;

namespace SystemMedia.Tests;

[TestFixture]
public sealed class ExactYouTubeInfoTests
{
	private const string Firefox = "firefox";

	[Test]
	public async Task The_widget_shows_the_watched_video_and_its_cover()
	{
		var cover = new MusicPlayerArtwork([7, 7, 7], "image/jpeg");
		var platform = Firefox_playing("Hovered video");
		using var corrector = Corrector(cover);
		using var player = Player(platform, corrector);

		await player.GetStateAsync();
		var state = await player.GetStateAsync();

		Assert.That(state.TrackName, Is.EqualTo("Watched video"));
		Assert.That(await player.GetArtworkAsync(state.ArtworkId!), Is.SameAs(cover));
	}

	[Test]
	public async Task Nothing_is_corrected_while_it_is_off()
	{
		var platform = Firefox_playing("Hovered video");
		using var corrector = new YouTubePageCorrector((_, _) => throw new AssertionException("looked up"), TimeProvider.System, Logger());
		using var player = Player(platform, corrector);

		Assert.That((await player.GetStateAsync()).TrackName, Is.EqualTo("Hovered video"));
	}

	[Test]
	public async Task A_page_after_a_corrected_video_still_has_the_leftover_cover_hidden()
	{
		var platform = Firefox_playing("Hovered video");
		using var corrector = Corrector(new MusicPlayerArtwork([7, 7, 7], "image/jpeg"));
		using var player = Player(platform, corrector);
		await player.GetStateAsync();
		await player.GetStateAsync();

		// TikTok publishes only a title; Firefox keeps the hovered video's image, byte for byte.
		platform.Sessions[Firefox] = FakeMediaPlatform.Playing(Firefox, "TikTok - Make Your Day");
		platform.BrowserTabs[Firefox] = [new BrowserTab("TikTok - Make Your Day", "www.tiktok.com/foryou", IsPlaying: true)];
		var state = await player.GetStateAsync();

		Assert.That(state.TrackName, Is.EqualTo("TikTok - Make Your Day"));
		Assert.That(state.ArtworkId, Is.EqualTo(AppIconArtwork.IdFor(Firefox)), "the app icon instead of the leftover");
	}

	private static FakeMediaPlatform Firefox_playing(string reportedTitle)
	{
		var platform = new FakeMediaPlatform { SystemCurrent = Firefox };
		platform.Sessions[Firefox] = FakeMediaPlatform.Playing(Firefox, reportedTitle);
		platform.BrowserTabs[Firefox] = [new BrowserTab("Watched video - YouTube", "www.youtube.com/watch?v=KNFDBYUeOYI", IsPlaying: true)];
		return platform;
	}

	private static YouTubePageCorrector Corrector(MusicPlayerArtwork cover) =>
		new((id, _) => Task.FromResult<YouTubeVideo?>(new YouTubeVideo(id, "Watched video", "Channel", cover)), TimeProvider.System, Logger()) { Enabled = true };

	private static SessionMusicPlayer Player(FakeMediaPlatform platform, YouTubePageCorrector corrector) =>
		new(platform, new CurrentAppSelection(platform, TimeProvider.System), Firefox, TimeProvider.System, corrector);

	private static Serilog.Core.Logger Logger() => new LoggerConfiguration().CreateLogger();

	[TestCase("true", true)]
	[TestCase("True", true)]
	[TestCase("false", false)]
	[TestCase("", null)]
	[TestCase(null, null)]
	public void The_settings_page_reads_its_stored_switch(string? stored, bool? expected) =>
		Assert.That(SettingsConfigFlow.ReadFlag(stored), Is.EqualTo(expected));

	[Test]
	public void An_opt_in_from_the_old_settings_file_is_still_read()
	{
		var directory = Path.Combine(Path.GetTempPath(), "system-media-tests-" + Guid.NewGuid().ToString("N"));
		try
		{
			Assert.That(new PluginSettingsStore(directory, Logger()).Load().ExactYouTubeInfo, Is.False);

			Directory.CreateDirectory(directory);
			File.WriteAllText(Path.Combine(directory, "settings.json"), """{"exactYouTubeInfo":true,"appCycleSeconds":15}""");

			Assert.That(new PluginSettingsStore(directory, Logger()).Load().ExactYouTubeInfo, Is.True);
		}
		finally
		{
			if (Directory.Exists(directory))
			{
				Directory.Delete(directory, recursive: true);
			}
		}
	}
}
