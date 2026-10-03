using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SystemMedia.Platform.Windows.Audio;

// Limited-information access is granted even for elevated processes.
internal static partial class ProcessInfo
{
	private const uint ProcessQueryLimitedInformation = 0x1000;
	private const int MaxPathChars = 32767;
	private const int PackageFamilyNameMaxChars = 65;

	internal static (string? ExecutableFileName, string? PackageFamilyName) Read(uint processId)
	{
		using var handle = OpenProcess(ProcessQueryLimitedInformation, inheritHandle: false, processId);
		if (handle.IsInvalid)
		{
			return (null, null);
		}

		return (ReadImagePath(handle) is { } path ? Path.GetFileName(path) : null, ReadPackageFamilyName(handle));
	}

	internal static string? ReadImagePath(uint processId)
	{
		using var handle = OpenProcess(ProcessQueryLimitedInformation, inheritHandle: false, processId);
		return handle.IsInvalid ? null : ReadImagePath(handle);
	}

	private static unsafe string? ReadImagePath(SafeProcessHandle handle)
	{
		var buffer = new char[MaxPathChars];
		var length = buffer.Length;
		fixed (char* start = buffer)
		{
			return QueryFullProcessImageNameW(handle, 0, start, ref length) ? new string(buffer, 0, length) : null;
		}
	}

	private static unsafe string? ReadPackageFamilyName(SafeProcessHandle handle)
	{
		var buffer = new char[PackageFamilyNameMaxChars];
		var length = (uint)buffer.Length;
		fixed (char* start = buffer)
		{
			// APPMODEL_ERROR_NO_PACKAGE for every ordinary desktop process.
			return GetPackageFamilyName(handle, ref length, start) == 0 && length > 1
				? new string(buffer, 0, (int)length - 1)
				: null;
		}
	}

	[LibraryImport("kernel32.dll", SetLastError = true)]
	private static partial SafeProcessHandle OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

	[LibraryImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static unsafe partial bool QueryFullProcessImageNameW(SafeProcessHandle process, uint flags, char* exeName, ref int size);

	[LibraryImport("kernel32.dll")]
	private static unsafe partial int GetPackageFamilyName(SafeProcessHandle process, ref uint packageFamilyNameLength, char* packageFamilyName);
}
