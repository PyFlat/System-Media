using NUnit.Framework;
using Serilog;
using SystemMedia.Platform.Linux;

namespace SystemMedia.Tests.Linux;

[TestFixture]
public sealed class LinuxMediaPlatformTests
{
	// The host asks for issues while the first start is still running.
	[Test]
	public void A_platform_that_has_not_started_yet_is_not_reported_unavailable()
	{
		using var platform = new LinuxMediaPlatform(new LoggerConfiguration().CreateLogger());

		Assert.That(platform.GetIssues(), Is.Empty);
	}
}
