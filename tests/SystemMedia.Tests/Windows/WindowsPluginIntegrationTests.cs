using MacroDeck.Plugin.Protocol.Capabilities.MusicPlayer;
using MacroDeck.Plugin.Testing;
using NUnit.Framework;
using Serilog;
using SystemMedia.Platform;
using SystemMedia.Platform.Windows.Audio;
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

	// A machine without a microphone or speakers has no default device, so only what holds either way is checked.
	[TestCase(nameof(AudioDeviceRole.Output))]
	[TestCase(nameof(AudioDeviceRole.Input))]
	[TestCase(nameof(AudioDeviceRole.CommunicationOutput))]
	[TestCase(nameof(AudioDeviceRole.CommunicationInput))]
	public async Task A_default_device_is_one_of_the_connected_devices(string roleName)
	{
		var role = Enum.Parse<AudioDeviceRole>(roleName);
		var devices = new WindowsAudioDevices();

		var connected = await devices.GetDevicesAsync(role.IsInput(), CancellationToken.None);
		var current = await devices.GetDefaultAsync(role, CancellationToken.None);

		Assert.That(connected.Select(device => device.Name), Has.None.Empty);
		if (current is not null)
		{
			Assert.That(connected, Has.Member(current));
		}
	}

	[Test]
	public async Task An_unknown_device_is_not_made_the_default()
	{
		var switched = await new WindowsAudioDevices().SetDefaultAsync(AudioDeviceRole.Output, "{0.0.0.00000000}.{unknown}", CancellationToken.None);

		Assert.That(switched, Is.False);
	}

	// The host asks for issues while the first start is still running.
	[Test]
	public void A_platform_that_has_not_started_yet_is_not_reported_unavailable()
	{
		using var platform = new WindowsMediaPlatform(new LoggerConfiguration().CreateLogger());

		Assert.That(platform.GetIssues().Select(issue => issue.Id), Has.No.Member(WindowsMediaPlatform.UnavailableIssueId));
	}
}
