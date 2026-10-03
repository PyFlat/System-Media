using System.Runtime.InteropServices;

namespace SystemMedia.Platform.MacOS;

// The Core Audio hardware properties (AudioHardware.h). A device is identified by its UID, which survives
// reconnects and restarts; its AudioObjectID does not.
internal sealed unsafe partial class CoreAudioDevices : IAudioDevices
{
	private const string CoreAudio = "/System/Library/Frameworks/CoreAudio.framework/CoreAudio";
	private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

	private const uint SystemObject = 1;
	private const uint ElementMain = 0;
	private static readonly uint _devices = FourCharCode("dev#");
	private static readonly uint _defaultOutput = FourCharCode("dOut");
	private static readonly uint _defaultInput = FourCharCode("dIn ");
	private static readonly uint _name = FourCharCode("lnam");
	private static readonly uint _uid = FourCharCode("uid ");
	private static readonly uint _streams = FourCharCode("stm#");
	private static readonly uint _scopeGlobal = FourCharCode("glob");
	private static readonly uint _scopeInput = FourCharCode("inpt");
	private static readonly uint _scopeOutput = FourCharCode("outp");

	public Task<IReadOnlyList<AudioDevice>> GetDevicesAsync(bool input, CancellationToken cancellationToken) =>
		Task.FromResult<IReadOnlyList<AudioDevice>>([.. GetDevices(input).Select(device => device.Device)]);

	// macOS has no separate defaults for calls.
	public Task<AudioDevice?> GetDefaultAsync(AudioDeviceRole role, CancellationToken cancellationToken)
	{
		var objectId = GetDefaultObjectId(role.IsInput());
		return Task.FromResult(objectId == 0 ? null : Read(objectId));
	}

	public Task<bool> SetDefaultAsync(AudioDeviceRole role, string deviceId, CancellationToken cancellationToken)
	{
		var match = GetDevices(role.IsInput()).FirstOrDefault(device => string.Equals(device.Device.Id, deviceId, StringComparison.Ordinal));
		if (match.Device is null)
		{
			return Task.FromResult(false);
		}

		var objectId = match.ObjectId;
		var address = new PropertyAddress(role.IsInput() ? _defaultInput : _defaultOutput, _scopeGlobal);
		var status = AudioObjectSetPropertyData(SystemObject, &address, 0, null, sizeof(uint), &objectId);
		return status == 0
			? Task.FromResult(true)
			: throw new InvalidOperationException($"Core Audio refused the default device change (status {status}).");
	}

	private static List<(uint ObjectId, AudioDevice Device)> GetDevices(bool input)
	{
		var address = new PropertyAddress(_devices, _scopeGlobal);
		uint size;
		if (AudioObjectGetPropertyDataSize(SystemObject, &address, 0, null, &size) != 0 || size == 0)
		{
			return [];
		}

		var objectIds = new uint[size / sizeof(uint)];
		fixed (uint* buffer = objectIds)
		{
			if (AudioObjectGetPropertyData(SystemObject, &address, 0, null, &size, buffer) != 0)
			{
				return [];
			}
		}

		var devices = new List<(uint, AudioDevice)>();
		foreach (var objectId in objectIds.AsSpan(0, (int)(size / sizeof(uint))))
		{
			if (HasStreams(objectId, input) && Read(objectId) is { } device)
			{
				devices.Add((objectId, device));
			}
		}

		return devices;
	}

	private static uint GetDefaultObjectId(bool input)
	{
		var address = new PropertyAddress(input ? _defaultInput : _defaultOutput, _scopeGlobal);
		uint objectId = 0;
		var size = (uint)sizeof(uint);
		return AudioObjectGetPropertyData(SystemObject, &address, 0, null, &size, &objectId) == 0 ? objectId : 0;
	}

	private static bool HasStreams(uint objectId, bool input)
	{
		var address = new PropertyAddress(_streams, input ? _scopeInput : _scopeOutput);
		uint size;
		return AudioObjectGetPropertyDataSize(objectId, &address, 0, null, &size) == 0 && size > 0;
	}

	private static AudioDevice? Read(uint objectId) =>
		ReadString(objectId, _uid) is { Length: > 0 } uid && ReadString(objectId, _name) is { Length: > 0 } name
			? new AudioDevice(uid, name)
			: null;

	// The caller owns the returned CFString.
	private static string? ReadString(uint objectId, uint selector)
	{
		var address = new PropertyAddress(selector, _scopeGlobal);
		IntPtr text = 0;
		var size = (uint)sizeof(IntPtr);
		if (AudioObjectGetPropertyData(objectId, &address, 0, null, &size, &text) != 0 || text == 0)
		{
			return null;
		}

		try
		{
			var length = CFStringGetLength(text);
			var characters = new char[length];
			fixed (char* buffer = characters)
			{
				CFStringGetCharacters(text, new CFRange(0, length), buffer);
			}

			return new string(characters);
		}
		finally
		{
			CFRelease(text);
		}
	}

	private static uint FourCharCode(string code) =>
		((uint)code[0] << 24) | ((uint)code[1] << 16) | ((uint)code[2] << 8) | code[3];

	[StructLayout(LayoutKind.Sequential)]
	private readonly struct PropertyAddress(uint selector, uint scope)
	{
		public readonly uint Selector = selector;
		public readonly uint Scope = scope;
		public readonly uint Element = ElementMain;
	}

	[StructLayout(LayoutKind.Sequential)]
	private readonly struct CFRange(nint location, nint length)
	{
		public readonly nint Location = location;
		public readonly nint Length = length;
	}

	[LibraryImport(CoreAudio)]
	private static partial int AudioObjectGetPropertyDataSize(uint objectId, PropertyAddress* address, uint qualifierSize, void* qualifier, uint* size);

	[LibraryImport(CoreAudio)]
	private static partial int AudioObjectGetPropertyData(uint objectId, PropertyAddress* address, uint qualifierSize, void* qualifier, uint* size, void* data);

	[LibraryImport(CoreAudio)]
	private static partial int AudioObjectSetPropertyData(uint objectId, PropertyAddress* address, uint qualifierSize, void* qualifier, uint size, void* data);

	[LibraryImport(CoreFoundation)]
	private static partial nint CFStringGetLength(IntPtr text);

	[LibraryImport(CoreFoundation)]
	private static partial void CFStringGetCharacters(IntPtr text, CFRange range, char* buffer);

	[LibraryImport(CoreFoundation)]
	private static partial void CFRelease(IntPtr value);
}
