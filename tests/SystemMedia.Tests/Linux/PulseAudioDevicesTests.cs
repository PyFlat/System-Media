using NUnit.Framework;
using SystemMedia.Platform;
using SystemMedia.Platform.Linux;

namespace SystemMedia.Tests.Linux;

[TestFixture]
public sealed class PulseAudioDevicesTests
{
	[Test]
	public void Sinks_are_named_by_their_description()
	{
		const string json = """
			[{"index":56,"state":"RUNNING","name":"alsa_output.pci-0000_00_1f.3.analog-stereo","description":"Built-in Audio Analog Stereo","monitor_source":"alsa_output.pci-0000_00_1f.3.analog-stereo.monitor"}]
			""";

		Assert.That(PulseAudioDevices.ParseDevices(json),
			Is.EqualTo(new[] { new AudioDevice("alsa_output.pci-0000_00_1f.3.analog-stereo", "Built-in Audio Analog Stereo") }));
	}

	[Test]
	public void A_monitor_of_a_sink_is_no_input()
	{
		const string json = """
			[
				{"name":"alsa_output.usb.monitor","description":"Monitor of Headset","monitor_of_sink":"alsa_output.usb"},
				{"name":"alsa_input.usb","description":"Headset Microphone","monitor_of_sink":"n/a"},
				{"name":"other.monitor","description":"Monitor without the field"}
			]
			""";

		Assert.That(PulseAudioDevices.ParseDevices(json).Single().Id, Is.EqualTo("alsa_input.usb"));
	}

	[Test]
	public void The_defaults_come_from_the_server_info()
	{
		const string json = """
			{"server_name":"PulseAudio (on PipeWire 1.2.7)","default_sink_name":"alsa_output.usb","default_source_name":"alsa_input.usb"}
			""";

		Assert.That(PulseAudioDevices.ParseDefaults(json), Is.EqualTo(("alsa_output.usb", "alsa_input.usb")));
	}
}
