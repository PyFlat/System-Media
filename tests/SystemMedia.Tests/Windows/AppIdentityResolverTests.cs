using NUnit.Framework;
using SystemMedia.Platform.Windows.Shell;

namespace SystemMedia.Tests.Windows;

[TestFixture]
public sealed class AppIdentityResolverTests
{
	[Test]
	public void An_id_the_shell_cannot_resolve_falls_back_instead_of_throwing()
	{
		var identity = new AppIdentityResolver(TimeProvider.System).Resolve("com.pyflat.not-an-installed-app!Nothing");

		Assert.That(identity.DisplayName, Is.EqualTo("Nothing"));
	}
}
