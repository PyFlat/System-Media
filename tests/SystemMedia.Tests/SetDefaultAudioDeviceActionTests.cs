using MacroDeck.Sdk.Actions;
using NUnit.Framework;
using Serilog;
using SystemMedia.Platform;

namespace SystemMedia.Tests;

[TestFixture]
public sealed class SetDefaultAudioDeviceActionTests
{
	private static readonly ILogger _logger = Serilog.Core.Logger.None;

	[Test]
	public async Task A_device_is_switched_to_by_its_id()
	{
		var devices = new FakeAudioDevices();

		var outcome = await SetDefaultAudioDeviceAction.SwitchAsync(devices, AudioDeviceRole.Output, "headset-out", _logger, CancellationToken.None);

		Assert.That(outcome, Is.EqualTo(SetDefaultAudioDeviceAction.Outcome.Switched));
		Assert.That(devices.Switched, Is.EqualTo((AudioDeviceRole.Output, "headset-out")));
	}

	[Test]
	public async Task A_device_is_switched_to_by_the_name_a_variable_shows()
	{
		var devices = new FakeAudioDevices();

		var outcome = await SetDefaultAudioDeviceAction.SwitchAsync(devices, AudioDeviceRole.CommunicationInput, " headset microphone ", _logger, CancellationToken.None);

		Assert.That(outcome, Is.EqualTo(SetDefaultAudioDeviceAction.Outcome.Switched));
		Assert.That(devices.Switched, Is.EqualTo((AudioDeviceRole.CommunicationInput, "headset-in")));
	}

	[Test]
	public async Task An_output_is_not_found_among_the_inputs()
	{
		var devices = new FakeAudioDevices();

		var outcome = await SetDefaultAudioDeviceAction.SwitchAsync(devices, AudioDeviceRole.Input, "headset-out", _logger, CancellationToken.None);

		Assert.That(outcome, Is.EqualTo(SetDefaultAudioDeviceAction.Outcome.NotFound));
		Assert.That(devices.Switched, Is.Null);
	}

	[Test]
	public async Task A_refused_switch_is_a_failure()
	{
		var devices = new FakeAudioDevices { Refuses = true };

		var outcome = await SetDefaultAudioDeviceAction.SwitchAsync(devices, AudioDeviceRole.Output, "speakers", _logger, CancellationToken.None);

		Assert.That(outcome, Is.EqualTo(SetDefaultAudioDeviceAction.Outcome.Failed));
	}

	[Test]
	public async Task A_platform_without_audio_devices_cannot_switch()
	{
		var outcome = await SetDefaultAudioDeviceAction.SwitchAsync(null, AudioDeviceRole.Output, "speakers", _logger, CancellationToken.None);

		Assert.That(outcome, Is.EqualTo(SetDefaultAudioDeviceAction.Outcome.NotSupported));
	}

	[Test]
	public async Task The_device_options_follow_the_chosen_role()
	{
		var action = new SetDefaultAudioDeviceAction(() => new FakeAudioDevices(), _logger);

		var result = await action.GetDynamicOptionsAsync(
			new DynamicOptionsContext
			{
				ParameterName = SetDefaultAudioDeviceAction.DeviceParameter,
				CurrentParameters = new Dictionary<string, object?> { [SetDefaultAudioDeviceAction.RoleParameter] = "communication-input" },
			},
			CancellationToken.None);

		Assert.That(result.Options.Single().Value, Is.EqualTo("headset-in"));
	}

	[TestCase("output", nameof(AudioDeviceRole.Output))]
	[TestCase("input", nameof(AudioDeviceRole.Input))]
	[TestCase("communication-output", nameof(AudioDeviceRole.CommunicationOutput))]
	[TestCase("communication-input", nameof(AudioDeviceRole.CommunicationInput))]
	public void The_stored_role_values_stay_the_same(string value, string role)
	{
		Assert.That(SetDefaultAudioDeviceAction.ParseRole(value).ToString(), Is.EqualTo(role));
	}

	private sealed class FakeAudioDevices : IAudioDevices
	{
		private static readonly AudioDevice[] _outputs = [new("speakers", "Speakers"), new("headset-out", "Headset")];
		private static readonly AudioDevice[] _inputs = [new("headset-in", "Headset Microphone")];

		public bool Refuses { get; init; }

		public (AudioDeviceRole, string)? Switched { get; private set; }

		public Task<IReadOnlyList<AudioDevice>> GetDevicesAsync(bool input, CancellationToken cancellationToken) =>
			Task.FromResult<IReadOnlyList<AudioDevice>>(input ? _inputs : _outputs);

		public Task<AudioDevice?> GetDefaultAsync(AudioDeviceRole role, CancellationToken cancellationToken) =>
			Task.FromResult<AudioDevice?>(role.IsInput() ? _inputs[0] : _outputs[0]);

		public Task<bool> SetDefaultAsync(AudioDeviceRole role, string deviceId, CancellationToken cancellationToken)
		{
			if (Refuses)
			{
				throw new InvalidOperationException("Refused.");
			}

			Switched = (role, deviceId);
			return Task.FromResult(true);
		}
	}
}
