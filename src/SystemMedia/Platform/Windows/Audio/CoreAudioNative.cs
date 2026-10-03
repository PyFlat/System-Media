using System.Runtime.InteropServices;

namespace SystemMedia.Platform.Windows.Audio;

// mmdeviceapi.h and audiopolicy.h. COM binds by vtable position, so every method up to the last one called
// is declared in header order.

[ComImport]
[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorComObject;

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
	[PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
}

[ComImport]
[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
	[PreserveSig] int GetCount(out int count);
	[PreserveSig] int Item(int index, out IMMDevice device);
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
	[PreserveSig] int Activate(ref Guid interfaceId, int classContext, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object result);
}

[ComImport]
[Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionManager2
{
	[PreserveSig] int GetAudioSessionControl(IntPtr sessionId, uint flags, out IntPtr control);
	[PreserveSig] int GetSimpleAudioVolume(IntPtr sessionId, uint flags, out IntPtr volume);
	[PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
}

[ComImport]
[Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEnumerator
{
	[PreserveSig] int GetCount(out int count);
	[PreserveSig] int GetSession(int index, out IAudioSessionControl2 session);
}

[ComImport]
[Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl2
{
	[PreserveSig] int GetState(out int state);
	[PreserveSig] int GetDisplayName(out IntPtr name);
	[PreserveSig] int SetDisplayName(IntPtr name, ref Guid eventContext);
	[PreserveSig] int GetIconPath(out IntPtr path);
	[PreserveSig] int SetIconPath(IntPtr path, ref Guid eventContext);
	[PreserveSig] int GetGroupingParam(out Guid groupingParam);
	[PreserveSig] int SetGroupingParam(ref Guid groupingParam, ref Guid eventContext);
	[PreserveSig] int RegisterAudioSessionNotification(IntPtr client);
	[PreserveSig] int UnregisterAudioSessionNotification(IntPtr client);
	[PreserveSig] int GetSessionIdentifier(out IntPtr identifier);
	[PreserveSig] int GetSessionInstanceIdentifier(out IntPtr identifier);
	[PreserveSig] int GetProcessId(out uint processId);
}

[ComImport]
[Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISimpleAudioVolume
{
	[PreserveSig] int SetMasterVolume(float level, ref Guid eventContext);
	[PreserveSig] int GetMasterVolume(out float level);
}

internal static class CoreAudioNative
{
	internal const int DataFlowRender = 0;
	internal const int DeviceStateActive = 0x1;
	internal const int ClassContextAll = 0x17;
	internal const int SessionStateExpired = 2;
}
