using NUnit.Framework;
using SystemMedia.Core;
using SystemMedia.Platform.Windows;

namespace SystemMedia.Tests.Windows;

[TestFixture]
public sealed class AppIconCacheTests
{
	[Test]
	public async Task A_desktop_app_s_icon_is_read_from_its_executable_as_png()
	{
		var executable = Environment.ProcessPath!;
		var app = new AppIdentity("test-runner.exe", "Test runner", executable, PackageFamilyName: null);

		var icon = await new AppIconCache().GetAsync(app, CancellationToken.None);

		Assert.That(icon, Is.Not.Null);
		Assert.That(icon!.MimeType, Is.EqualTo("image/png"));
		Assert.That(ArtworkFormat.SniffContentType(icon.Data), Is.EqualTo("image/png"));
	}

	[Test]
	public async Task An_app_with_nothing_to_take_an_icon_from_has_none()
	{
		var icon = await new AppIconCache().GetAsync(AppIdentity.Fallback("Chrome"), CancellationToken.None);

		Assert.That(icon, Is.Null);
	}

}
