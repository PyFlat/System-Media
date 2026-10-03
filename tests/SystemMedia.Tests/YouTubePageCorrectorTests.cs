using MacroDeck.Sdk.MusicPlayer;
using NUnit.Framework;
using Serilog;
using SystemMedia.Core;

namespace SystemMedia.Tests;

[TestFixture]
public sealed class YouTubePageCorrectorTests
{
	private const string Firefox = "308046B0AF4A39CB";
	private const string WatchedId = "KNFDBYUeOYI";
	private const string WatchedTitle = "Who Will Win Globals?";

	private static readonly MusicPlayerArtwork _watchedCover = new([1, 2, 3], "image/jpeg");

	private readonly List<string> _lookedUp = [];

	[SetUp]
	public void ForgetLookups() => _lookedUp.Clear();

	[Test]
	public void Nothing_changes_while_it_is_off()
	{
		using var corrector = Create(enabled: false);

		var result = corrector.Correct(Hijacked(), [PlayingTab(inFront: true)]);

		Assert.That(result.Title, Is.EqualTo("Hovered video"));
		Assert.That(_lookedUp, Is.Empty);
	}

	[Test]
	public void The_watched_video_replaces_the_hovered_one()
	{
		using var corrector = Create();
		corrector.Correct(Hijacked(), [PlayingTab(inFront: true)]);

		var result = corrector.Correct(Hijacked(), [PlayingTab(inFront: true)]);

		Assert.That(result.Title, Is.EqualTo(WatchedTitle));
		Assert.That(result.Artist, Is.EqualTo("AussieAntics"));
		Assert.That(result.HasThumbnail, Is.True);
		Assert.That(corrector.CorrectedThumbnail(Firefox), Is.SameAs(_watchedCover));
		Assert.That(result.EndTime, Is.EqualTo(TimeSpan.FromMinutes(20)), "the timeline is the watched video's already");
	}

	[Test]
	public void Only_the_title_is_corrected_until_the_lookup_is_done()
	{
		using var corrector = Create(lookUp: (_, _) => new TaskCompletionSource<YouTubeVideo?>().Task);

		var result = corrector.Correct(Hijacked(), [PlayingTab(inFront: true)]);

		Assert.That(result.Title, Is.EqualTo(WatchedTitle));
		Assert.That(result.Artist, Is.Null);
		Assert.That(result.HasThumbnail, Is.False);
	}

	[Test]
	public void A_video_id_seen_in_front_is_used_once_the_tab_is_behind_another()
	{
		using var corrector = Create();
		corrector.Correct(Reported(WatchedTitle), [PlayingTab(inFront: true)]);
		corrector.Correct(Hijacked(), [PlayingTab(inFront: false)]);

		var result = corrector.Correct(Hijacked(), [PlayingTab(inFront: false)]);

		Assert.That(result.Artist, Is.EqualTo("AussieAntics"));
	}

	[Test]
	public void Matching_info_is_left_as_reported()
	{
		using var corrector = Create();

		var result = corrector.Correct(Reported(WatchedTitle), [PlayingTab(inFront: true)]);

		Assert.That(result, Is.EqualTo(Reported(WatchedTitle)));
		Assert.That(_lookedUp, Is.Empty);
		Assert.That(corrector.CorrectedThumbnail(Firefox), Is.Null);
	}

	[Test]
	public void Another_tab_playing_too_is_never_renamed_after_the_video()
	{
		using var corrector = Create();
		var twitch = new BrowserTab("AussieAntics - Twitch", "www.twitch.tv/aussieantics", IsPlaying: true);

		var result = corrector.Correct(Reported("AussieAntics - Twitch"), [PlayingTab(inFront: false), twitch]);

		Assert.That(result.Title, Is.EqualTo("AussieAntics - Twitch"));
	}

	[Test]
	public void Media_from_a_tab_that_is_not_youtube_is_left_alone()
	{
		using var corrector = Create();
		var twitch = new BrowserTab("AussieAntics - Twitch", "www.twitch.tv/aussieantics", IsPlaying: true);

		var result = corrector.Correct(Reported("AussieAntics"), [PlayingTab(inFront: false) with { IsPlaying = false }, twitch]);

		Assert.That(result.Title, Is.EqualTo("AussieAntics"));
	}

	[Test]
	public void A_paused_video_is_still_corrected_while_its_tab_is_open()
	{
		using var corrector = Create();
		corrector.Correct(Hijacked(), [PlayingTab(inFront: true)]);

		var result = corrector.Correct(Hijacked() with { Status = PlaybackState.Paused }, [PlayingTab(inFront: true) with { IsPlaying = false }]);

		Assert.That(result.Title, Is.EqualTo(WatchedTitle));
	}

	[Test]
	public void A_fullscreen_window_known_only_by_its_title_is_still_corrected()
	{
		using var corrector = Create();
		corrector.Correct(Hijacked(), [PlayingTab(inFront: true)]);

		var result = corrector.Correct(Hijacked(), [new BrowserTab("(2) " + WatchedTitle + " - YouTube - Google Chrome", null, IsPlaying: false)]);

		Assert.That(result.Title, Is.EqualTo(WatchedTitle));
		Assert.That(result.Artist, Is.EqualTo("AussieAntics"));
	}

	[Test]
	public void A_closed_tab_is_no_longer_corrected_for()
	{
		using var corrector = Create();
		corrector.Correct(Hijacked(), [PlayingTab(inFront: true)]);

		var result = corrector.Correct(Hijacked() with { Status = PlaybackState.Paused }, [new BrowserTab("Google", "www.google.com", IsPlaying: false)]);

		Assert.That(result.Title, Is.EqualTo("Hovered video"));
	}

	[Test]
	public void A_lookup_for_another_video_than_the_title_names_is_not_trusted()
	{
		using var corrector = Create(lookUp: (id, _) => Task.FromResult<YouTubeVideo?>(new YouTubeVideo(id, "The next video", "Someone", _watchedCover)));
		corrector.Correct(Hijacked(), [PlayingTab(inFront: true)]);

		var result = corrector.Correct(Hijacked(), [PlayingTab(inFront: true)]);

		Assert.That(result.Title, Is.EqualTo(WatchedTitle));
		Assert.That(result.Artist, Is.Null);
	}

	[Test]
	public void A_failed_lookup_is_not_repeated_on_every_read()
	{
		using var corrector = Create(lookUp: (id, _) =>
		{
			_lookedUp.Add(id);
			return Task.FromResult<YouTubeVideo?>(null);
		});

		corrector.Correct(Hijacked(), [PlayingTab(inFront: true)]);
		corrector.Correct(Hijacked(), [PlayingTab(inFront: true)]);

		Assert.That(_lookedUp, Has.Count.EqualTo(1));
	}

	private YouTubePageCorrector Create(bool enabled = true, Func<string, CancellationToken, Task<YouTubeVideo?>>? lookUp = null) =>
		new(lookUp ?? LookUp, TimeProvider.System, new LoggerConfiguration().CreateLogger()) { Enabled = enabled };

	private Task<YouTubeVideo?> LookUp(string id, CancellationToken cancellationToken)
	{
		_lookedUp.Add(id);
		return Task.FromResult<YouTubeVideo?>(id == WatchedId ? new YouTubeVideo(id, WatchedTitle, "AussieAntics", _watchedCover) : null);
	}

	private static BrowserTab PlayingTab(bool inFront) =>
		new("(2) " + WatchedTitle + " - YouTube", inFront ? "www.youtube.com/watch?v=" + WatchedId : null, IsPlaying: true);

	private static MediaSnapshot Hijacked() => Reported("Hovered video") with { Artist = "Other channel", HasThumbnail = true };

	private static MediaSnapshot Reported(string title) => new()
	{
		AppId = Firefox,
		AppName = "Firefox",
		Title = title,
		Status = PlaybackState.Playing,
		EndTime = TimeSpan.FromMinutes(20),
		Position = TimeSpan.FromMinutes(3),
	};
}
