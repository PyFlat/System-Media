using System.Runtime.InteropServices;

namespace SystemMedia.Platform.Windows.Audio;

internal sealed partial class WindowsAudioDevices : IAudioDevices
{
	// PKEY_Device_FriendlyName, the name the Sound settings show.
	private static readonly Guid _friendlyNameFormat = new("A45C254E-DF1C-4EFD-8020-67D146A850E0");
	private const int FriendlyNameId = 14;

	public Task<IReadOnlyList<AudioDevice>> GetDevicesAsync(bool input, CancellationToken cancellationToken) =>
		Task.FromResult<IReadOnlyList<AudioDevice>>(GetDevices(input));

	public Task<AudioDevice?> GetDefaultAsync(AudioDeviceRole role, CancellationToken cancellationToken) =>
		Task.FromResult(GetDefault(role));

	public Task<bool> SetDefaultAsync(AudioDeviceRole role, string deviceId, CancellationToken cancellationToken)
	{
		if (!GetDevices(role.IsInput()).Any(device => string.Equals(device.Id, deviceId, StringComparison.Ordinal)))
		{
			return Task.FromResult(false);
		}

		var policy = (IPolicyConfig)new PolicyConfigComObject();
		try
		{
			// The Sound settings' "default device" is both the console and the multimedia role.
			int[] roles = role.IsCommunication()
				? [CoreAudioNative.RoleCommunications]
				: [CoreAudioNative.RoleConsole, CoreAudioNative.RoleMultimedia];
			foreach (var windowsRole in roles)
			{
				Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(deviceId, windowsRole));
			}

			return Task.FromResult(true);
		}
		finally
		{
			Marshal.ReleaseComObject(policy);
		}
	}

	private static List<AudioDevice> GetDevices(bool input)
	{
		var devices = new List<AudioDevice>();
		WithEnumerator(enumerator =>
		{
			if (enumerator.EnumAudioEndpoints(DataFlow(input), CoreAudioNative.DeviceStateActive, out var collection) != 0)
			{
				return;
			}

			try
			{
				if (collection.GetCount(out var count) != 0)
				{
					return;
				}

				for (var i = 0; i < count; i++)
				{
					if (collection.Item(i, out var device) == 0 && Read(device) is { } read)
					{
						devices.Add(read);
					}
				}
			}
			finally
			{
				Marshal.ReleaseComObject(collection);
			}
		});

		return devices;
	}

	private static AudioDevice? GetDefault(AudioDeviceRole role)
	{
		AudioDevice? result = null;
		var windowsRole = role.IsCommunication() ? CoreAudioNative.RoleCommunications : CoreAudioNative.RoleConsole;
		WithEnumerator(enumerator =>
		{
			// Fails when no device of that kind is connected.
			if (enumerator.GetDefaultAudioEndpoint(DataFlow(role.IsInput()), windowsRole, out var device) == 0)
			{
				result = Read(device);
			}
		});

		return result;
	}

	private static int DataFlow(bool input) => input ? CoreAudioNative.DataFlowCapture : CoreAudioNative.DataFlowRender;

	private static void WithEnumerator(Action<IMMDeviceEnumerator> use)
	{
		IMMDeviceEnumerator enumerator;
		try
		{
			enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
		}
		catch (COMException)
		{
			// No audio service: no devices.
			return;
		}

		try
		{
			use(enumerator);
		}
		finally
		{
			Marshal.ReleaseComObject(enumerator);
		}
	}

	// Releases the device.
	private static AudioDevice? Read(IMMDevice device)
	{
		IPropertyStore? properties = null;
		try
		{
			if (device.GetId(out var id) != 0 || device.OpenPropertyStore(CoreAudioNative.StorageRead, out properties) != 0)
			{
				return null;
			}

			var key = new PropertyKey(_friendlyNameFormat, FriendlyNameId);
			if (properties.GetValue(ref key, out var value) != 0)
			{
				return null;
			}

			try
			{
				return value.VariantType == CoreAudioNative.VariantTypeString && Marshal.PtrToStringUni(value.Value) is { Length: > 0 } name
					? new AudioDevice(id, name)
					: null;
			}
			finally
			{
				_ = PropVariantClear(ref value);
			}
		}
		finally
		{
			if (properties is not null)
			{
				Marshal.ReleaseComObject(properties);
			}

			Marshal.ReleaseComObject(device);
		}
	}

	[LibraryImport("ole32.dll")]
	private static partial int PropVariantClear(ref PropVariant value);
}
