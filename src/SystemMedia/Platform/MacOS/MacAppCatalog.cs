using System.Collections.Concurrent;
using System.Diagnostics;
using MacroDeck.Sdk.MusicPlayer;
using SystemMedia.Core;

namespace SystemMedia.Platform.MacOS;

// Names are needed on every read, so a lookup never blocks: the bundle id stands in until it finishes.
internal sealed class MacAppCatalog
{
	private const int IconSize = 256;
	private static readonly TimeSpan _toolTimeout = TimeSpan.FromSeconds(5);

	private readonly ConcurrentDictionary<string, AppIdentity> _resolved = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<string, byte> _pending = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<string, Lazy<Task<MusicPlayerArtwork?>>> _icons = new(StringComparer.Ordinal);

	internal event EventHandler? Resolved;

	internal AppIdentity Resolve(string bundleId)
	{
		if (_resolved.TryGetValue(bundleId, out var identity))
		{
			return identity;
		}

		if (_pending.TryAdd(bundleId, 0))
		{
			_ = LookUpAsync(bundleId);
		}

		return new AppIdentity(bundleId, bundleId, ExecutablePath: null, PackageFamilyName: null) { IsFallback = true };
	}

	// On macOS, ExecutablePath holds the app bundle's path.
	internal static bool HasIcon(AppIdentity app) => app.ExecutablePath is not null;

	internal Task<MusicPlayerArtwork?> GetIconAsync(AppIdentity app, CancellationToken cancellationToken) =>
		app.ExecutablePath is { } bundle
			? _icons.GetOrAdd(bundle, path => new Lazy<Task<MusicPlayerArtwork?>>(() => LoadIconAsync(path))).Value.WaitAsync(cancellationToken)
			: Task.FromResult<MusicPlayerArtwork?>(null);

	private async Task LookUpAsync(string bundleId)
	{
		try
		{
			// Bundle ids are reverse-DNS names; anything else is not passed into a Spotlight query.
			if (bundleId.Any(character => !(char.IsLetterOrDigit(character) || character is '.' or '-' or '_')))
			{
				return;
			}

			var output = await RunToolAsync("/usr/bin/mdfind", $"kMDItemCFBundleIdentifier == '{bundleId}'");
			var bundle = output?.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.FirstOrDefault(path => path.EndsWith(".app", StringComparison.OrdinalIgnoreCase));
			if (bundle is not null)
			{
				_resolved[bundleId] = new AppIdentity(bundleId, Path.GetFileNameWithoutExtension(bundle), bundle, PackageFamilyName: null);
				Resolved?.Invoke(this, EventArgs.Empty);
			}
		}
		finally
		{
			_pending.TryRemove(bundleId, out _);
		}
	}

	private static async Task<MusicPlayerArtwork?> LoadIconAsync(string bundle)
	{
		var iconName = Path.GetFileName((await RunToolAsync("/usr/bin/plutil", "-extract", "CFBundleIconFile", "raw", "-o", "-",
			Path.Combine(bundle, "Contents", "Info.plist")))?.Trim());
		if (string.IsNullOrEmpty(iconName))
		{
			return null;
		}

		var icns = Path.Combine(bundle, "Contents", "Resources", iconName.EndsWith(".icns", StringComparison.OrdinalIgnoreCase) ? iconName : iconName + ".icns");
		if (!File.Exists(icns))
		{
			return null;
		}

		var png = Path.Combine(Path.GetTempPath(), $"system-media-icon-{Guid.NewGuid():N}.png");
		try
		{
			await RunToolAsync("/usr/bin/sips", "-s", "format", "png", "--resampleHeightWidthMax", IconSize.ToString(System.Globalization.CultureInfo.InvariantCulture), icns, "--out", png);
			return File.Exists(png) && await File.ReadAllBytesAsync(png) is { Length: > 0 } bytes ? new MusicPlayerArtwork(bytes, "image/png") : null;
		}
		finally
		{
			File.Delete(png);
		}
	}

	private static async Task<string?> RunToolAsync(string tool, params string[] arguments)
	{
		var start = new ProcessStartInfo(tool) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
		foreach (var argument in arguments)
		{
			start.ArgumentList.Add(argument);
		}

		Process? process = null;
		try
		{
			process = Process.Start(start);
			if (process is null)
			{
				return null;
			}

			using var timeout = new CancellationTokenSource(_toolTimeout);
			var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
			await process.WaitForExitAsync(timeout.Token);
			return process.ExitCode == 0 ? output : null;
		}
		catch (OperationCanceledException)
		{
			process?.Kill(entireProcessTree: true);
			return null;
		}
		catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
		{
			return null;
		}
		finally
		{
			process?.Dispose();
		}
	}
}
