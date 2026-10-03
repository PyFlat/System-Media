using MacroDeck.Plugin.Protocol.Capabilities.MusicPlayer;
using MacroDeck.Plugin.Testing;
using NUnit.Framework;
using Serilog;
using SystemMedia.Platform.Windows;

namespace SystemMedia.Tests.Windows;

// Runs against the machine's real media sessions, so only asserts what holds whether or not something plays.
[TestFixture]
public sealed class WindowsPluginIntegrationTests
{
	[Test]
	public async Task The_current_instance_reports_a_connected_state_even_when_nothing_plays()
	{
		await using var harness = PluginIntegrationTests.CreateHarness();
		await harness.InitializeIntegrationsAsync();

		var state = (await harness.MusicPlayer.GetStateAsync(new MusicPlayerInstanceArguments { InstanceId = "current" }))
			.DataAs<MusicPlayerStateDto>();

		Assert.That(state!.IsConnected, Is.True);
	}

	// The host asks for issues while the first start is still running.
	[Test]
	public void A_platform_that_has_not_started_yet_is_not_reported_unavailable()
	{
		using var platform = new WindowsMediaPlatform(new LoggerConfiguration().CreateLogger());

		Assert.That(platform.GetIssues().Select(issue => issue.Id), Has.No.Member(WindowsMediaPlatform.UnavailableIssueId));
	}
}
