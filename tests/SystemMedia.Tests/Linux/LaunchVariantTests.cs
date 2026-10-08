using NUnit.Framework;
using SystemMedia.Platform.Linux;

namespace SystemMedia.Tests.Linux;

[TestFixture]
public sealed class LaunchVariantTests
{
	[Test]
	public void A_linux_mint_web_app_is_named_by_its_window_class() =>
		Assert.That(Variant(
			"/usr/lib/firefox/firefox", "--class", "WebApp-SpotifyWeb3778", "--name", "WebApp-SpotifyWeb3778",
			"--profile", "/home/user/.local/share/ice/firefox/SpotifyWeb3778", "--no-remote", "https://open.spotify.com/"),
			Is.EqualTo("WebApp-SpotifyWeb3778"));

	[TestCase("--profile", "/home/me/.mozilla/firefox/abcd.work/", ExpectedResult = "abcd.work")]
	[TestCase("-profile", "/home/me/.mozilla/firefox/abcd.work", ExpectedResult = "abcd.work")]
	[TestCase("--profile=/home/me/.mozilla/firefox/abcd.work", null, ExpectedResult = "abcd.work")]
	[TestCase("-P", "work", ExpectedResult = "work")]
	[TestCase("--user-data-dir=/home/me/.config/chromium-work", null, ExpectedResult = "chromium-work")]
	public string? A_second_profile_is_named_by_its_profile(string option, string? value) =>
		Variant(value is null ? ["/usr/lib/firefox/firefox", option] : ["/usr/lib/firefox/firefox", option, value]);

	[Test]
	public void A_plain_launch_is_no_variant()
	{
		Assert.That(Variant("/usr/lib/firefox/firefox", "https://example.com"), Is.Null);
		Assert.That(Variant("/usr/lib/firefox/firefox", "-P", "--new-window"), Is.Null, "-P alone opens the profile manager");
		Assert.That(Variant(), Is.Null);
	}

	[Test]
	public void A_class_that_is_the_app_itself_is_no_variant() =>
		Assert.That(Variant("/usr/lib/firefox/firefox", "--class", "Firefox"), Is.Null);

	private static string? Variant(params string[] arguments) => LaunchVariant.Of(arguments, "firefox");
}
