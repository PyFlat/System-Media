using System.Runtime.InteropServices;
using SystemMedia.Platform.Windows.Audio;

namespace SystemMedia.Platform.Windows.Browser;

// Window classes are shared with other apps (Thunderbird, every Electron app) and hidden windows, so a window
// also has to be visible, titled and owned by the browser's executable.
internal static partial class BrowserWindows
{
	internal static IReadOnlyList<(IntPtr Handle, string Title)> Find(BrowserUi browser)
	{
		var windows = new List<(IntPtr, string)>();
		var processNames = new Dictionary<uint, bool>();
		EnumWindows((handle, _) =>
		{
			if (IsWindowVisible(handle) &&
				ClassNameOf(handle) == browser.WindowClass &&
				TitleOf(handle) is { Length: > 0 } title &&
				IsOwnedBy(browser.ExecutableFileName, handle, processNames))
			{
				windows.Add((handle, title));
			}

			return true;
		}, IntPtr.Zero);

		return windows;
	}

	private static bool IsOwnedBy(string executableFileName, IntPtr handle, Dictionary<uint, bool> known)
	{
		_ = GetWindowThreadProcessId(handle, out var processId);
		if (!known.TryGetValue(processId, out var owned))
		{
			owned = string.Equals(ProcessInfo.Read(processId).ExecutableFileName, executableFileName, StringComparison.OrdinalIgnoreCase);
			known[processId] = owned;
		}

		return owned;
	}

	private static unsafe string ClassNameOf(IntPtr handle)
	{
		var buffer = stackalloc char[64];
		var length = GetClassNameW(handle, buffer, 64);
		return length > 0 ? new string(buffer, 0, length) : string.Empty;
	}

	private static unsafe string? TitleOf(IntPtr handle)
	{
		var length = GetWindowTextLengthW(handle);
		if (length <= 0)
		{
			return null;
		}

		var buffer = new char[length + 1];
		fixed (char* start = buffer)
		{
			var read = GetWindowTextW(handle, start, buffer.Length);
			return read > 0 ? new string(buffer, 0, read) : null;
		}
	}

	private delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

	[LibraryImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool IsWindowVisible(IntPtr handle);

	[LibraryImport("user32.dll")]
	private static unsafe partial int GetClassNameW(IntPtr handle, char* className, int maxCount);

	[LibraryImport("user32.dll")]
	private static partial int GetWindowTextLengthW(IntPtr handle);

	[LibraryImport("user32.dll")]
	private static unsafe partial int GetWindowTextW(IntPtr handle, char* text, int maxCount);

	[LibraryImport("user32.dll")]
	private static partial uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
}
