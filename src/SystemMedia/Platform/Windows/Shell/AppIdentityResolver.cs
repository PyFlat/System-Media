using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SystemMedia.Platform.Windows.Audio;

using SystemMedia.Core;

namespace SystemMedia.Platform.Windows.Shell;

// The shell's AppsFolder covers desktop apps too, which AppInfo.GetFromAppUserModelId does not. A fallback
// is cached only briefly, so an app that was closed gets its real name once it starts.
internal sealed class AppIdentityResolver(TimeProvider time)
{
	private static readonly TimeSpan _fallbackLifetime = TimeSpan.FromSeconds(30);

	private readonly ConcurrentDictionary<string, AppIdentity> _resolved = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<string, (AppIdentity Identity, DateTimeOffset Expires)> _fallbacks = new(StringComparer.Ordinal);

	internal AppIdentity Resolve(string appUserModelId)
	{
		if (_resolved.TryGetValue(appUserModelId, out var resolved))
		{
			return resolved;
		}

		var now = time.GetUtcNow();
		if (_fallbacks.TryGetValue(appUserModelId, out var fallback) && fallback.Expires > now)
		{
			return fallback.Identity;
		}

		var identity = FromShell(appUserModelId) ?? FromBrowserProfile(appUserModelId) ?? FromRunningProcess(appUserModelId) ?? AppIdentity.Fallback(appUserModelId);
		if (identity.IsFallback)
		{
			_fallbacks[appUserModelId] = (identity, now + _fallbackLifetime);
		}
		else
		{
			_resolved[appUserModelId] = identity;
			_fallbacks.TryRemove(appUserModelId, out _);
		}

		return identity;
	}

	private static AppIdentity? FromShell(string appUserModelId)
	{
		IShellItem2? item = null;
		try
		{
			var folderId = ShellNative.FolderIdAppsFolder;
			var itemId = typeof(IShellItem2).GUID;
			if (ShellNative.SHCreateItemInKnownFolder(ref folderId, 0, appUserModelId, ref itemId, out item) != 0 || item is null)
			{
				return null;
			}

			var name = item.GetDisplayName(ShellNative.SigdnNormalDisplay, out var displayName) == 0 ? displayName : null;
			if (string.IsNullOrWhiteSpace(name))
			{
				return null;
			}

			return new AppIdentity(
				appUserModelId,
				name,
				ReadString(item, ShellNative.PropertyKeyLinkTargetParsingPath),
				ReadString(item, ShellNative.PropertyKeyPackageFamilyName) ?? AppIdentity.PackageFamilyNameOf(appUserModelId));
		}
		catch (COMException)
		{
			return null;
		}
		finally
		{
			if (item is not null)
			{
				Marshal.ReleaseComObject(item);
			}
		}
	}

	// Chromium browsers publish a non-default profile as "<browser id>.<profile path>" (Chrome.UserData.Profile1),
	// which the shell only knows as the browser's own id.
	private static AppIdentity? FromBrowserProfile(string appUserModelId)
	{
		var separator = appUserModelId.IndexOf('.', StringComparison.Ordinal);
		if (separator <= 0 || appUserModelId.Contains('!', StringComparison.Ordinal) ||
			appUserModelId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		return FromShell(appUserModelId[..separator]) is { } browser ? browser with { AppId = appUserModelId } : null;
	}

	// For ids like vlc.exe. The version resource's description is what Task Manager shows.
	private static AppIdentity? FromRunningProcess(string appUserModelId)
	{
		if (!appUserModelId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
			appUserModelId.IndexOfAny(['\\', '/']) >= 0)
		{
			return null;
		}

		var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(appUserModelId));
		try
		{
			foreach (var process in processes)
			{
				if (ProcessInfo.ReadImagePath((uint)process.Id) is not { } path)
				{
					continue;
				}

				var version = FileVersionInfo.GetVersionInfo(path);
				var name = !string.IsNullOrWhiteSpace(version.FileDescription) ? version.FileDescription
					: !string.IsNullOrWhiteSpace(version.ProductName) ? version.ProductName
					: null;
				if (name is not null)
				{
					return new AppIdentity(appUserModelId, name.Trim(), path, PackageFamilyName: null);
				}
			}

			return null;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return null;
		}
		finally
		{
			foreach (var process in processes)
			{
				process.Dispose();
			}
		}
	}

	private static string? ReadString(IShellItem2 item, PropertyKey key) =>
		item.GetString(ref key, out var value) == 0 && !string.IsNullOrWhiteSpace(value) ? value : null;
}
