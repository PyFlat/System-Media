using NUnit.Framework;
using SystemMedia.Core;

namespace SystemMedia.Tests;

[TestFixture]
public sealed class YouTubePageTests
{
	[TestCase("Lofi Beats - YouTube", "Lofi Beats")]
	[TestCase("(3) Lofi Beats - YouTube", "Lofi Beats")]
	[TestCase("(99+) Lofi Beats - YouTube", "Lofi Beats")]
	[TestCase("Who Will Win? | Part 2 - YouTube", "Who Will Win? | Part 2")]
	[TestCase("(2) FNCS Globals - YouTube – Arbeitsspeichernutzung – 609 MB", "FNCS Globals")]
	[TestCase("FNCS Globals - YouTube - Audio playing - Memory usage - 21.9 MB", "FNCS Globals")]
	[TestCase("FNCS Globals - YouTube - Audiowiedergabe - Arbeitsspeichernutzung - 22,9 MB", "FNCS Globals")]
	[TestCase("FNCS Globals - YouTube — Mozilla Firefox", "FNCS Globals")]
	[TestCase("FNCS Globals - YouTube - Google Chrome", "FNCS Globals")]
	public void A_video_page_title_names_the_video(string pageTitle, string videoTitle) =>
		Assert.That(YouTubePage.VideoTitleOf(pageTitle), Is.EqualTo(videoTitle));

	[TestCase("YouTube")]
	[TestCase("AussieAntics - Twitch")]
	[TestCase("Song - YouTube Music")]
	[TestCase(" - YouTube")]
	[TestCase("YouTube - Google Chrome")]
	[TestCase("Best of - YouTube Rewind")]
	public void Other_pages_name_no_video(string pageTitle) =>
		Assert.That(YouTubePage.VideoTitleOf(pageTitle), Is.Null);

	[TestCase("www.youtube.com/watch?v=KNFDBYUeOYI", "KNFDBYUeOYI")]
	[TestCase("https://www.youtube.com/watch?list=RD1&v=n61ULEU7CO0&t=30s", "n61ULEU7CO0")]
	[TestCase("youtube.com/shorts/abcdefghijk", "abcdefghijk")]
	[TestCase("https://youtu.be/KNFDBYUeOYI?t=5", "KNFDBYUeOYI")]
	[TestCase("m.youtube.com/live/KNFDBYUeOYI", "KNFDBYUeOYI")]
	public void The_video_id_comes_from_the_address(string url, string id) =>
		Assert.That(YouTubePage.VideoIdOf(url), Is.EqualTo(id));

	[TestCase(null)]
	[TestCase("")]
	[TestCase("www.youtube.com/results?search_query=lofi")]
	[TestCase("www.youtube.com/watch?v=short")]
	[TestCase("www.twitch.tv/watch?v=KNFDBYUeOYI")]
	[TestCase("lofi beats being typed")]
	public void Anything_else_has_no_video_id(string? url) =>
		Assert.That(YouTubePage.VideoIdOf(url), Is.Null);
}
