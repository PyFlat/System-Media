using NUnit.Framework;
using SystemMedia.Core;

namespace SystemMedia.Tests;

[TestFixture]
public sealed class AppIdentityTests
{
	[TestCase("Spotify.exe", "Spotify")]
	[TestCase(@"{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}\VideoLAN\VLC\vlc.exe", "vlc")]
	[TestCase("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", "Spotify")]
	[TestCase("308046B0AF4A39CB", "308046B0AF4A39CB")]
	public void An_app_the_start_menu_does_not_know_still_gets_a_readable_name(string appUserModelId, string expected)
	{
		Assert.That(AppIdentity.Fallback(appUserModelId).DisplayName, Is.EqualTo(expected));
	}

	[Test]
	public void A_packaged_app_id_carries_its_package_family_name()
	{
		Assert.That(AppIdentity.PackageFamilyNameOf("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify"),
			Is.EqualTo("SpotifyAB.SpotifyMusic_zpdnekdrzrea0"));
		Assert.That(AppIdentity.PackageFamilyNameOf("Spotify.exe"), Is.Null);
	}

	[Test]
	public void A_desktop_app_is_matched_to_its_processes_by_executable_file_name()
	{
		var fromShortcut = new AppIdentity("308046B0AF4A39CB", "Firefox", @"C:\Program Files\Mozilla Firefox\firefox.exe", null);

		Assert.That(fromShortcut.ExecutableFileName, Is.EqualTo("firefox.exe"));
		Assert.That(AppIdentity.Fallback("Spotify.exe").ExecutableFileName, Is.EqualTo("Spotify.exe"));
		Assert.That(AppIdentity.Fallback("Chrome").ExecutableFileName, Is.Null);
	}

	[TestCase(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A }, "image/png")]
	[TestCase(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "image/jpeg")]
	[TestCase(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 }, "image/webp")]
	[TestCase(new byte[] { 0x00, 0x01 }, "application/octet-stream")]
	public void Artwork_without_a_content_type_is_identified_by_its_signature(byte[] bytes, string expected)
	{
		Assert.That(ArtworkFormat.SniffContentType(bytes), Is.EqualTo(expected));
	}
}
