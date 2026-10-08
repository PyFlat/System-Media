using System.Collections.Concurrent;
using MacroDeck.Sdk.MusicPlayer;

namespace SystemMedia.Platform.Linux;

// Only PNGs: nothing guarantees the deck can show an SVG.
internal sealed class DesktopEntries(IReadOnlyList<string> dataDirectories)
{
	private static readonly string[] _iconSizes = ["256x256", "512x512", "192x192", "128x128", "96x96", "64x64", "48x48"];

	private readonly ConcurrentDictionary<string, string?> _iconPaths = new(StringComparer.Ordinal);

	internal static DesktopEntries ForCurrentUser() => new(DataDirectories(Environment.GetEnvironmentVariable));

	// Flatpak and Snap exports are added in case the session's XDG_DATA_DIRS lacks them.
	internal static IReadOnlyList<string> DataDirectories(Func<string, string?> environment)
	{
		var home = environment("HOME") ?? string.Empty;
		var dataHome = environment("XDG_DATA_HOME") is { Length: > 0 } configuredHome ? configuredHome : Path.Combine(home, ".local", "share");
		var dataDirs = environment("XDG_DATA_DIRS") is { Length: > 0 } configured ? configured : "/usr/local/share:/usr/share";
		return
		[
			.. new[] { dataHome }
				.Concat(dataDirs.Split(':', StringSplitOptions.RemoveEmptyEntries))
				.Concat([Path.Combine(dataHome, "flatpak", "exports", "share"), "/var/lib/flatpak/exports/share", "/var/lib/snapd/desktop"])
				.Where(Path.IsPathRooted)
				.Distinct(StringComparer.Ordinal),
		];
	}

	// The player reports its own DesktopEntry, so one that is not a plain file name is ignored.
	internal string? IconPath(string? desktopEntry, string appId)
	{
		var entry = desktopEntry is not null && IsFileName(desktopEntry) ? desktopEntry : null;
		return _iconPaths.GetOrAdd(entry ?? appId, _ => FindIcon(entry, appId));
	}

	private static bool IsFileName(string name) =>
		name.Length > 0 && name is not ("." or "..") && name.AsSpan().IndexOfAny('/', '\\') < 0;

	internal static async Task<MusicPlayerArtwork?> LoadAsync(string iconPath, CancellationToken cancellationToken)
	{
		try
		{
			return new MusicPlayerArtwork(await File.ReadAllBytesAsync(iconPath, cancellationToken), "image/png");
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}

	internal string? NameOf(string desktopEntry) => IsFileName(desktopEntry) ? ValueOf(desktopEntry, "Name") : null;

	// Desktop actions carry keys of their own.
	internal static string? ValueOf(IEnumerable<string> desktopFileLines, string key)
	{
		var prefix = key + "=";
		var inEntry = false;
		foreach (var raw in desktopFileLines)
		{
			var line = raw.Trim();
			if (line.StartsWith('['))
			{
				inEntry = string.Equals(line, "[Desktop Entry]", StringComparison.Ordinal);
				continue;
			}

			if (inEntry && line.StartsWith(prefix, StringComparison.Ordinal) && line.Length > prefix.Length)
			{
				return line[prefix.Length..].Trim();
			}
		}

		return null;
	}

	private string? FindIcon(string? desktopEntry, string appId)
	{
		var iconName = ValueOf(desktopEntry ?? appId, "Icon") ?? ValueOf(appId.ToLowerInvariant(), "Icon") ?? appId.ToLowerInvariant();
		if (Path.IsPathRooted(iconName))
		{
			return iconName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && File.Exists(iconName) ? iconName : null;
		}

		var candidates = dataDirectories
			.SelectMany(directory => _iconSizes.Select(size => Path.Combine(directory, "icons", "hicolor", size, "apps", iconName + ".png")))
			.Concat(dataDirectories.Select(directory => Path.Combine(directory, "pixmaps", iconName + ".png")));
		return candidates.FirstOrDefault(File.Exists);
	}

	private string? ValueOf(string desktopEntry, string key)
	{
		var fileName = desktopEntry.EndsWith(".desktop", StringComparison.Ordinal) ? desktopEntry : desktopEntry + ".desktop";
		foreach (var directory in dataDirectories)
		{
			var path = Path.Combine(directory, "applications", fileName);
			if (!File.Exists(path))
			{
				continue;
			}

			try
			{
				return ValueOf(File.ReadLines(path), key);
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				return null;
			}
		}

		return null;
	}
}
