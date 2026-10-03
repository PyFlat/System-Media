using NUnit.Framework;
using SystemMedia.Players;

namespace SystemMedia.Tests.Players;

[TestFixture]
public sealed class LocalSocketsTests
{
	[Test]
	public void A_pattern_matches_the_last_segment_of_a_pipe_named_like_a_path()
	{
		var pattern = LocalSockets.PatternToRegex("smplayer-mpv-*");

		Assert.That(pattern.IsMatch(LocalSockets.LastSegment(@"C:\Users\someone\AppData\Local\Temp\smplayer-mpv-bd0c")), Is.True);
		Assert.That(pattern.IsMatch(LocalSockets.LastSegment("/var/folders/ab/T/smplayer-mpv-1f2")), Is.True);
		Assert.That(pattern.IsMatch("mpvsocket"), Is.False);
	}

	[Test]
	public void Only_names_with_wildcards_are_patterns()
	{
		Assert.That(LocalSockets.IsPattern("qtsingleapp-smplay-*"), Is.True);
		Assert.That(LocalSockets.IsPattern("mpvsocket"), Is.False);
	}

	[Test]
	public void An_endpoint_that_does_not_exist_is_not_found()
	{
		Assert.That(LocalSockets.Find(["sm-test-missing-" + Guid.NewGuid().ToString("N"), "sm-test-missing-*-" + Guid.NewGuid().ToString("N")]), Is.Empty);
	}
}
