using System.Diagnostics;
using System.Globalization;

namespace SystemMedia.Platform.MacOS;

// Since macOS 15.4 only Apple-entitled processes may read Now Playing. The user installs media-control from
// Homebrew, which runs mediaremote-adapter inside /usr/bin/perl; nothing third-party ships with the plugin.
internal sealed class MediaControl
{
	internal const string InstallCommand = "brew install media-control";

	// A process started by macOS does not inherit the shell's PATH, so Homebrew's folders are named explicitly.
	internal static readonly string[] SearchPaths = ["/opt/homebrew/bin/media-control", "/usr/local/bin/media-control"];

	private static readonly TimeSpan _commandTimeout = TimeSpan.FromSeconds(5);

	internal static string? Locate() => SearchPaths.FirstOrDefault(File.Exists);

	// The diffing stream, or the full artwork would be printed again on every position update.
	internal static Process StartStream() =>
		Process.Start(Create("stream", "--micros", "--debounce=100"))
			?? throw new InvalidOperationException("The media-control stream could not be started.");

	internal static Task<bool> SendAsync(int commandId, CancellationToken cancellationToken) =>
		RunAsync(cancellationToken, "send", commandId.ToString(CultureInfo.InvariantCulture));

	internal static Task<bool> SeekAsync(TimeSpan position, CancellationToken cancellationToken) =>
		RunAsync(cancellationToken, "seek", "--micros", ((long)position.TotalMicroseconds).ToString(CultureInfo.InvariantCulture));

	internal static Task<bool> SetShuffleAsync(int mode, CancellationToken cancellationToken) =>
		RunAsync(cancellationToken, "shuffle", mode.ToString(CultureInfo.InvariantCulture));

	internal static Task<bool> SetRepeatAsync(int mode, CancellationToken cancellationToken) =>
		RunAsync(cancellationToken, "repeat", mode.ToString(CultureInfo.InvariantCulture));

	private static async Task<bool> RunAsync(CancellationToken cancellationToken, params string[] arguments)
	{
		using var process = Process.Start(Create(arguments));
		if (process is null)
		{
			return false;
		}

		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(_commandTimeout);
		try
		{
			await process.WaitForExitAsync(timeout.Token);
			return process.ExitCode == 0;
		}
		catch (OperationCanceledException)
		{
			process.Kill(entireProcessTree: true);
			if (cancellationToken.IsCancellationRequested)
			{
				throw;
			}

			return false;
		}
	}

	private static ProcessStartInfo Create(params string[] arguments)
	{
		var executable = Locate() ?? throw new InvalidOperationException("media-control is not installed.");
		var start = new ProcessStartInfo(executable)
		{
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true,
		};
		foreach (var argument in arguments)
		{
			start.ArgumentList.Add(argument);
		}

		return start;
	}
}
