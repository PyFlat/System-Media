using System.Runtime.InteropServices;

namespace SystemMedia.Platform.Windows.Shell;

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey(Guid formatId, uint propertyId)
{
	public Guid FormatId = formatId;
	public uint PropertyId = propertyId;
}

// Unused slots are declared so the vtable order matches shobjidl_core.h.
[ComImport]
[Guid("7e9fb0d3-919f-4307-ab2e-9b1860310c93")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem2
{
	[PreserveSig] int BindToHandler(IntPtr bindContext, ref Guid handlerId, ref Guid interfaceId, out IntPtr result);
	[PreserveSig] int GetParent(out IntPtr parent);
	[PreserveSig] int GetDisplayName(uint form, [MarshalAs(UnmanagedType.LPWStr)] out string name);
	[PreserveSig] int GetAttributes(uint mask, out uint attributes);
	[PreserveSig] int Compare(IntPtr other, uint hint, out int order);
	[PreserveSig] int GetPropertyStore(int flags, ref Guid interfaceId, out IntPtr store);
	[PreserveSig] int GetPropertyStoreWithCreateObject(int flags, IntPtr createObject, ref Guid interfaceId, out IntPtr store);
	[PreserveSig] int GetPropertyStoreForKeys(IntPtr keys, uint count, int flags, ref Guid interfaceId, out IntPtr store);
	[PreserveSig] int GetPropertyDescriptionList(ref PropertyKey type, ref Guid interfaceId, out IntPtr list);
	[PreserveSig] int Update(IntPtr bindContext);
	[PreserveSig] int GetProperty(ref PropertyKey key, IntPtr value);
	[PreserveSig] int GetCLSID(ref PropertyKey key, out Guid classId);
	[PreserveSig] int GetFileTime(ref PropertyKey key, out long fileTime);
	[PreserveSig] int GetInt32(ref PropertyKey key, out int value);
	[PreserveSig] int GetString(ref PropertyKey key, [MarshalAs(UnmanagedType.LPWStr)] out string value);
}

internal static class ShellNative
{
	internal const uint SigdnNormalDisplay = 0;

	internal static readonly Guid FolderIdAppsFolder = new("1e87508d-89c2-42f0-8a7e-645a0f50ca58");

	internal static readonly PropertyKey PropertyKeyLinkTargetParsingPath =
		new(new Guid("b9b4b3fc-2b51-4a42-b5d8-324146afcf25"), 2);

	internal static readonly PropertyKey PropertyKeyPackageFamilyName =
		new(new Guid("9f4c2855-9f79-4b39-a8d0-e1d42de1d5f3"), 17);

#pragma warning disable SYSLIB1054 // LibraryImport cannot marshal a [ComImport] interface.
	[DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
	internal static extern int SHCreateItemInKnownFolder(
		ref Guid folderId,
		uint flags,
		string itemName,
		ref Guid interfaceId,
		[MarshalAs(UnmanagedType.Interface)] out IShellItem2? item);
#pragma warning restore SYSLIB1054
}
