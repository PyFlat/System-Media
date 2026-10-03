using System.Text;
using Microsoft.Win32;

namespace SystemMedia.Platform.Windows.Vlc;

internal enum VlcSetupState
{
	NotInstalled,
	AddOnMissing,
	AddOnDisabled,
	Ready,
}

internal sealed record VlcInstallation(string InstallDirectory, bool Is32Bit)
{
	internal string AddOnDirectory => Path.Combine(InstallDirectory, "plugins", "misc");

	// VLC's architecture, not Windows'.
	internal string Architecture => Is32Bit ? "x86" : "x64";
}

// VLC only publishes to the Windows media controls with the vlc-win10smtc add-on.
internal static class VlcSetup
{
	internal const string ReleasesUrl = "https://github.com/spmn/vlc-win10smtc/releases/latest";

	private const string AddOnFileName = "libwin10smtc_plugin.dll";
	private const string ModuleName = "win10smtc";
	private const string ControlKey = "control=";

	internal static string SettingsPath =>
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "vlc", "vlcrc");

	internal static (VlcSetupState State, VlcInstallation? Installation) Check()
	{
		if (FindInstallation() is not { } installation)
		{
			return (VlcSetupState.NotInstalled, null);
		}

		if (!File.Exists(Path.Combine(installation.AddOnDirectory, AddOnFileName)))
		{
			return (VlcSetupState.AddOnMissing, installation);
		}

		var settings = File.Exists(SettingsPath) ? File.ReadAllText(SettingsPath) : string.Empty;
		return (IsEnabled(settings) ? VlcSetupState.Ready : VlcSetupState.AddOnDisabled, installation);
	}

	// VLC reads the file at start, so it has to be restarted to pick this up.
	internal static void Enable()
	{
		var path = SettingsPath;
		var exists = File.Exists(path);
		var hasBom = exists && HasUtf8Bom(path);
		var settings = exists ? File.ReadAllText(path) : string.Empty;
		if (IsEnabled(settings))
		{
			return;
		}

		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, WithAddOnEnabled(settings), new UTF8Encoding(hasBom));
	}

	internal static bool IsEnabled(string settings) =>
		ActiveControlLine(Lines(settings)) is { } line &&
		line[ControlKey.Length..].Split([':', ','], StringSplitOptions.TrimEntries).Contains(ModuleName, StringComparer.OrdinalIgnoreCase);

	// VLC writes every option commented out at its default under its section; control belongs to [core].
	internal static string WithAddOnEnabled(string settings)
	{
		var newline = settings.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
		var lines = Lines(settings);

		var active = lines.FindIndex(IsActiveControlLine);
		if (active >= 0)
		{
			var value = lines[active].Trim()[ControlKey.Length..].Trim();
			lines[active] = ControlKey + (value.Length == 0 ? ModuleName : value + ":" + ModuleName);
			return string.Join(newline, lines);
		}

		var commented = lines.FindIndex(line => line.TrimStart().StartsWith("#" + ControlKey, StringComparison.Ordinal));
		if (commented >= 0)
		{
			lines[commented] = ControlKey + ModuleName;
			return string.Join(newline, lines);
		}

		var core = lines.FindIndex(line => string.Equals(line.Trim(), "[core]", StringComparison.OrdinalIgnoreCase));
		if (core >= 0)
		{
			lines.Insert(core + 1, ControlKey + ModuleName);
			return string.Join(newline, lines);
		}

		var prefix = settings.Length == 0 || settings.EndsWith('\n') ? settings : settings + newline;
		return prefix + "[core]" + newline + ControlKey + ModuleName + newline;
	}

	private static VlcInstallation? FindInstallation()
	{
		foreach (var (view, is32Bit) in new[] { (RegistryView.Registry64, false), (RegistryView.Registry32, true) })
		{
			using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
			using var key = root.OpenSubKey(@"SOFTWARE\VideoLAN\VLC");
			if (key?.GetValue("InstallDir") is string directory && Directory.Exists(directory))
			{
				return new VlcInstallation(directory, is32Bit);
			}
		}

		return null;
	}

	private static List<string> Lines(string settings) => [.. settings.Split('\n').Select(line => line.TrimEnd('\r'))];

	private static string? ActiveControlLine(List<string> lines) =>
		lines.Find(IsActiveControlLine)?.Trim();

	private static bool IsActiveControlLine(string line) =>
		line.TrimStart().StartsWith(ControlKey, StringComparison.Ordinal);

	private static bool HasUtf8Bom(string path)
	{
		Span<byte> start = stackalloc byte[3];
		using var stream = File.OpenRead(path);
		return stream.Read(start) == 3 && start is [0xEF, 0xBB, 0xBF];
	}
}
