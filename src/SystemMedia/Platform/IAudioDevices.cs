namespace SystemMedia.Platform;

// Only Windows keeps separate defaults for calls; elsewhere the communication roles are the plain defaults.
internal enum AudioDeviceRole
{
	Output,
	Input,
	CommunicationOutput,
	CommunicationInput,
}

internal sealed record AudioDevice(string Id, string Name);

// Reads return no device when the system has none; only a platform-level failure throws.
internal interface IAudioDevices
{
	Task<IReadOnlyList<AudioDevice>> GetDevicesAsync(bool input, CancellationToken cancellationToken);

	Task<AudioDevice?> GetDefaultAsync(AudioDeviceRole role, CancellationToken cancellationToken);

	// false when no device has that id.
	Task<bool> SetDefaultAsync(AudioDeviceRole role, string deviceId, CancellationToken cancellationToken);
}

internal static class AudioDeviceRoles
{
	internal static bool IsInput(this AudioDeviceRole role) =>
		role is AudioDeviceRole.Input or AudioDeviceRole.CommunicationInput;

	internal static bool IsCommunication(this AudioDeviceRole role) =>
		role is AudioDeviceRole.CommunicationOutput or AudioDeviceRole.CommunicationInput;

	// A stored id first, then a name, so a variable can be set to what it shows.
	internal static async Task<AudioDevice?> FindAsync(this IAudioDevices devices, AudioDeviceRole role, string idOrName,
		CancellationToken cancellationToken)
	{
		var candidates = await devices.GetDevicesAsync(role.IsInput(), cancellationToken);
		return candidates.FirstOrDefault(device => string.Equals(device.Id, idOrName, StringComparison.Ordinal))
			?? candidates.FirstOrDefault(device => string.Equals(device.Name, idOrName.Trim(), StringComparison.OrdinalIgnoreCase));
	}
}
