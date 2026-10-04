using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using Serilog;
using SystemMedia.Platform;

namespace SystemMedia;

internal sealed class SetDefaultAudioDeviceAction(Func<IAudioDevices?> devices, ILogger logger) : IDynamicOptionsActionDefinition
{
	internal const string ActionId = "set-default-audio-device";

	// Parameter names and role values are stored in users' buttons.
	internal const string RoleParameter = "role";
	internal const string DeviceParameter = "device";

	private static readonly (string Value, AudioDeviceRole Role)[] _roles =
	[
		("output", AudioDeviceRole.Output),
		("input", AudioDeviceRole.Input),
		("communication-output", AudioDeviceRole.CommunicationOutput),
		("communication-input", AudioDeviceRole.CommunicationInput),
	];

	internal enum Outcome
	{
		Switched,
		NotSupported,
		NotFound,
		Failed,
	}

	public string Id => ActionId;

	public LocalizedText Name => Strings.Actions.SetDefaultAudioDevice.Name();

	public LocalizedText Description => Strings.Actions.SetDefaultAudioDevice.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Choice(
			RoleParameter,
			[
				new ActionParameterOption { Value = "output", Label = Strings.Actions.SetDefaultAudioDevice.Role.Output() },
				new ActionParameterOption { Value = "input", Label = Strings.Actions.SetDefaultAudioDevice.Role.Input() },
				new ActionParameterOption { Value = "communication-output", Label = Strings.Actions.SetDefaultAudioDevice.Role.CommunicationOutput() },
				new ActionParameterOption { Value = "communication-input", Label = Strings.Actions.SetDefaultAudioDevice.Role.CommunicationInput() },
			],
			Strings.Actions.SetDefaultAudioDevice.Role.Label(),
			Strings.Actions.SetDefaultAudioDevice.Role.Description(),
			defaultValue: "output",
			required: true),
		ActionParameter.DynamicChoice(DeviceParameter, Strings.Actions.SetDefaultAudioDevice.Device.Label(), required: true),
	];

	public IActionExecutor CreateExecutor() => new Executor(devices, logger);

	public async Task<DynamicOptionsResult> GetDynamicOptionsAsync(DynamicOptionsContext context, CancellationToken cancellationToken)
	{
		if (!string.Equals(context.ParameterName, DeviceParameter, StringComparison.Ordinal))
		{
			return new DynamicOptionsResult { Options = [] };
		}

		if (devices() is not { } audio)
		{
			return new DynamicOptionsResult { Options = [], Error = Strings.Actions.SetDefaultAudioDevice.NotSupported() };
		}

		var role = ParseRole(context.CurrentParameters.GetValueOrDefault(RoleParameter)) ?? AudioDeviceRole.Output;
		var available = await audio.GetDevicesAsync(role.IsInput(), cancellationToken);
		return new DynamicOptionsResult
		{
			Options = [.. available.Select(device => new ActionParameterOption { Value = device.Id, Label = device.Name })],
			CacheSeconds = 2,
		};
	}

	internal static AudioDeviceRole? ParseRole(object? value)
	{
		var text = value?.ToString();
		foreach (var (candidate, role) in _roles)
		{
			if (string.Equals(candidate, text, StringComparison.Ordinal))
			{
				return role;
			}
		}

		return null;
	}

	internal static async Task<Outcome> SwitchAsync(IAudioDevices? devices, AudioDeviceRole role, string idOrName, ILogger logger,
		CancellationToken cancellationToken)
	{
		if (devices is null)
		{
			return Outcome.NotSupported;
		}

		try
		{
			return await devices.FindAsync(role, idOrName, cancellationToken) is { } device &&
				await devices.SetDefaultAsync(role, device.Id, cancellationToken)
					? Outcome.Switched
					: Outcome.NotFound;
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			logger.Warning(exception, "Setting the default {Role} device to {Device} failed: {Message}", role, idOrName, exception.Message);
			return Outcome.Failed;
		}
	}

	private sealed class Executor(Func<IAudioDevices?> devices, ILogger logger) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var role = ParseRole(context.Parameters.GetValueOrDefault(RoleParameter) ?? "output");
			if (role is null || context.Parameters.GetValueOrDefault(DeviceParameter)?.ToString() is not { Length: > 0 } device)
			{
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter, Strings.Actions.SetDefaultAudioDevice.NoDevice());
			}

			return await SwitchAsync(devices(), role.Value, device, logger, context.CancellationToken) switch
			{
				Outcome.Switched => ActionResult.Success(),
				Outcome.NotSupported => ActionResult.Failed(ActionErrorCodes.Unavailable, Strings.Actions.SetDefaultAudioDevice.NotSupported()),
				Outcome.NotFound => ActionResult.Failed(ActionErrorCodes.NotFound, Strings.Actions.SetDefaultAudioDevice.DeviceNotFound()),
				_ => ActionResult.Failed(ActionErrorCodes.ProviderError, Strings.Actions.SetDefaultAudioDevice.Failed()),
			};
		}
	}
}
