using NUnit.Framework;
using MacroDeck.Sdk.MusicPlayer;
using SystemMedia.Core;

namespace SystemMedia.Tests;

[TestFixture]
public sealed class StaleMediaFilterTests
{
	private static readonly DateTimeOffset _now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
	private static readonly DateTimeOffset _spotifyStamp = _now.AddSeconds(-30);

	[Test]
	public void The_first_track_seen_is_trusted_as_reported()
	{
		var filter = NewFilter();

		var result = filter.Apply(Spotify(), "cover-a", _now);

		Assert.That(result.HasThumbnail, Is.True);
		Assert.That(result.EndTime, Is.EqualTo(TimeSpan.FromMinutes(3)));
	}

	[Test]
	public void A_timeline_left_over_from_the_previous_track_is_hidden()
	{
		var filter = NewFilter();
		filter.Apply(Spotify(), "cover-a", _now);

		var result = filter.Apply(TikTok(), "cover-a", _now.AddSeconds(1));

		Assert.That(result.EndTime, Is.EqualTo(TimeSpan.Zero));
		Assert.That(result.Position, Is.EqualTo(TimeSpan.Zero));
	}

	[Test]
	public void The_timeline_comes_back_once_the_app_reports_a_new_one()
	{
		var filter = NewFilter();
		filter.Apply(Spotify(), "cover-a", _now);
		filter.Apply(TikTok(), "cover-a", _now.AddSeconds(1));

		var restamped = TikTok() with { EndTime = TimeSpan.FromSeconds(40), Position = TimeSpan.FromSeconds(2), LastUpdatedTime = _now.AddSeconds(2) };
		var result = filter.Apply(restamped, null, _now.AddSeconds(2));

		Assert.That(result.EndTime, Is.EqualTo(TimeSpan.FromSeconds(40)));
	}

	[Test]
	public void A_next_track_that_restamped_its_timeline_is_trusted_immediately()
	{
		var filter = NewFilter();
		filter.Apply(Spotify(), "cover-a", _now);

		var next = Spotify() with { Title = "Next Song", AlbumTitle = "Other Album", EndTime = TimeSpan.FromMinutes(4), LastUpdatedTime = _now };
		var result = filter.Apply(next, "cover-b", _now.AddSeconds(1));

		Assert.That(result.EndTime, Is.EqualTo(TimeSpan.FromMinutes(4)));
		Assert.That(result.HasThumbnail, Is.True);
	}

	[Test]
	public void A_track_that_comes_back_before_the_timeline_changed_keeps_its_own_timeline()
	{
		var filter = NewFilter();
		filter.Apply(Spotify(), "cover-a", _now);
		filter.Apply(TikTok(), "cover-b", _now.AddSeconds(1));

		var result = filter.Apply(Spotify(), "cover-a", _now.AddSeconds(2));

		Assert.That(result.EndTime, Is.EqualTo(TimeSpan.FromMinutes(3)));
	}

	[Test]
	public void A_thumbnail_left_over_from_the_previous_track_is_hidden()
	{
		var filter = NewFilter();
		filter.Apply(Spotify(), "cover-a", _now);

		var result = filter.Apply(TikTok(), "cover-a", _now.AddSeconds(1));

		Assert.That(result.HasThumbnail, Is.False);
	}

	[Test]
	public void Two_tracks_of_one_album_may_share_their_cover()
	{
		var filter = NewFilter();
		filter.Apply(Spotify(), "cover-a", _now);

		var sameAlbum = Spotify() with { Title = "Track Two", LastUpdatedTime = _now };
		var result = filter.Apply(sameAlbum, "cover-a", _now.AddSeconds(1));

		Assert.That(result.HasThumbnail, Is.True);
	}

	[Test]
	public void A_stream_keeps_its_channel_picture_when_its_title_changes()
	{
		var filter = NewFilter();
		var stream = Spotify() with { Title = "DROPS ON", Artist = "AussieAntics", AlbumTitle = null };
		filter.Apply(stream, "channel-picture", _now);

		var retitled = stream with { Title = "WATCHING FNCS GLOBALS", LastUpdatedTime = _now };
		var result = filter.Apply(retitled, "channel-picture", _now.AddSeconds(1));

		Assert.That(result.HasThumbnail, Is.True);
	}

	[Test]
	public void Going_back_to_a_stream_shows_its_picture_again_though_the_browser_never_resent_it()
	{
		var filter = NewFilter();
		var twitch = Spotify() with { Title = "DROPS ON", Artist = "AussieAntics", AlbumTitle = null };
		filter.Apply(twitch, "channel-picture", _now);
		var tiktok = filter.Apply(TikTok() with { LastUpdatedTime = _now.AddSeconds(1) }, "channel-picture", _now.AddSeconds(1));

		var back = filter.Apply(twitch with { LastUpdatedTime = _now.AddSeconds(2) }, "channel-picture", _now.AddSeconds(2));

		Assert.That(tiktok.HasThumbnail, Is.False, "TikTok shows the app icon, not the channel picture");
		Assert.That(back.HasThumbnail, Is.True);
	}

	[Test]
	public void A_hidden_thumbnail_comes_back_once_the_app_replaces_it()
	{
		var filter = NewFilter();
		filter.Apply(Spotify(), "cover-a", _now);
		filter.Apply(TikTok(), "cover-a", _now.AddSeconds(1));

		var later = _now.AddSeconds(1) + StaleMediaFilter.StaleThumbnailRecheck;
		Assert.That(filter.NeedsThumbnailHash(TikTok(), later), Is.True);
		var result = filter.Apply(TikTok(), "tiktok-cover", later);

		Assert.That(result.HasThumbnail, Is.True);
	}

	[Test]
	public void A_thumbnail_left_over_from_an_older_track_is_hidden_too()
	{
		var filter = NewFilter();
		filter.Apply(Spotify(), "cover-a", _now);
		var video = Spotify() with { Title = "A video", Artist = "A channel", AlbumTitle = null, LastUpdatedTime = _now };
		filter.Apply(video, "video-thumbnail", _now.AddSeconds(1));

		var result = filter.Apply(TikTok(), "cover-a", _now.AddSeconds(2));

		Assert.That(result.HasThumbnail, Is.False);
	}

	[Test]
	public void A_cover_seen_before_a_restart_is_still_known_as_another_tracks()
	{
		var directory = Directory.CreateTempSubdirectory("system-media-tests-").FullName;
		try
		{
			new StaleMediaFilter(new ThumbnailHistory(directory, Serilog.Core.Logger.None)).Apply(Spotify(), "cover-a", _now);

			var afterRestart = new StaleMediaFilter(new ThumbnailHistory(directory, Serilog.Core.Logger.None));
			var video = Spotify() with { Title = "DROPS ON", Artist = "AussieAntics", AlbumTitle = null, LastUpdatedTime = _now };
			afterRestart.Apply(video, "channel-picture", _now.AddSeconds(1));
			var result = afterRestart.Apply(TikTok(), "cover-a", _now.AddSeconds(2));

			Assert.That(result.HasThumbnail, Is.False);
			Assert.That(File.ReadAllText(Path.Combine(directory, "thumbnail-history.json")), Does.Not.Contain("Night Transit"));
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[Test]
	public void A_cover_first_seen_on_a_title_only_page_belongs_to_the_song_that_shows_it_later()
	{
		var filter = NewFilter();
		var firstTikTok = filter.Apply(TikTok(), "cover-a", _now);

		var song = filter.Apply(Spotify(), "cover-a", _now.AddSeconds(1));
		var nextTikTok = filter.Apply(TikTok() with { Title = "TikTok - another video" }, "cover-a", _now.AddSeconds(2));

		Assert.That(firstTikTok.HasThumbnail, Is.True, "nothing tells the first sighting apart");
		Assert.That(song.HasThumbnail, Is.True);
		Assert.That(nextTikTok.HasThumbnail, Is.False);
	}

	[Test]
	public void An_unchanged_track_does_not_reread_its_thumbnail()
	{
		var filter = NewFilter();
		filter.Apply(Spotify(), "cover-a", _now);

		Assert.That(filter.NeedsThumbnailHash(Spotify(), _now.AddSeconds(1)), Is.False);
		Assert.That(filter.NeedsThumbnailHash(TikTok(), _now.AddSeconds(1)), Is.True);
	}

	private static MediaSnapshot Spotify() => new()
	{
		AppId = "308046B0AF4A39CB",
		AppName = "Firefox",
		Title = "Night Transit",
		Artist = "The Artist",
		AlbumTitle = "Test Album",
		HasThumbnail = true,
		Status = PlaybackState.Playing,
		EndTime = TimeSpan.FromMinutes(3),
		Position = TimeSpan.FromSeconds(30),
		LastUpdatedTime = _spotifyStamp,
	};

	private static StaleMediaFilter NewFilter() => new(ThumbnailHistory.InMemory());

	// Same session, new page: only the title changed, the rest is what Spotify left.
	private static MediaSnapshot TikTok() => Spotify() with { Title = "TikTok - Make Your Day", Artist = null, AlbumTitle = null };
}
