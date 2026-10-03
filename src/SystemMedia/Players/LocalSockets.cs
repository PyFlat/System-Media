using System.IO.Pipes;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace SystemMedia.Players;

// Named pipes on Windows, Unix sockets elsewhere. A name with * or ? is matched against the last path
// segment, because players such as SMPlayer put their process id into it.
internal static class LocalSockets
{
	private const string PipePrefix = @"\\.\pipe\";
	private static readonly TimeSpan _connectTimeout = TimeSpan.FromSeconds(1);

	internal static IReadOnlyList<string> Find(IReadOnlyList<string> names)
	{
		var found = new List<string>();
		IReadOnlyList<string>? candidates = null;
		foreach (var name in names)
		{
			if (!IsForThisSystem(name))
			{
				continue;
			}

			if (!IsPattern(name))
			{
				if (Exists(Normalize(name)))
				{
					found.Add(Normalize(name));
				}

				continue;
			}

			candidates ??= ListEndpoints();
			var pattern = PatternToRegex(name);
			found.AddRange(candidates.Where(candidate => pattern.IsMatch(LastSegment(candidate))));
		}

		return [.. found.Distinct(StringComparer.Ordinal)];
	}

	internal static async Task<Stream> ConnectAsync(string endpoint, CancellationToken cancellationToken)
	{
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(_connectTimeout);
		if (OperatingSystem.IsWindows())
		{
			var pipe = new NamedPipeClientStream(".", endpoint, PipeDirection.InOut, PipeOptions.Asynchronous);
			try
			{
				await pipe.ConnectAsync(timeout.Token);
				return pipe;
			}
			catch
			{
				await pipe.DisposeAsync();
				throw;
			}
		}

		var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
		try
		{
			await socket.ConnectAsync(new UnixDomainSocketEndPoint(endpoint), timeout.Token);
			return new NetworkStream(socket, ownsSocket: true);
		}
		catch
		{
			socket.Dispose();
			throw;
		}
	}

	internal static bool IsPattern(string name) => name.AsSpan().IndexOfAny('*', '?') >= 0;

	internal static Regex PatternToRegex(string pattern) =>
		new("^" + Regex.Escape(LastSegment(pattern)).Replace(@"\*", ".*", StringComparison.Ordinal).Replace(@"\?", ".", StringComparison.Ordinal) + "$",
			RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

	internal static string LastSegment(string name)
	{
		var separator = name.LastIndexOfAny(['\\', '/']);
		return separator >= 0 ? name[(separator + 1)..] : name;
	}

	private static bool IsForThisSystem(string name) =>
		OperatingSystem.IsWindows() ? !name.StartsWith('/') : !name.StartsWith(PipePrefix, StringComparison.OrdinalIgnoreCase);

	private static string Normalize(string name)
	{
		if (OperatingSystem.IsWindows())
		{
			return name.StartsWith(PipePrefix, StringComparison.OrdinalIgnoreCase) ? name[PipePrefix.Length..] : name;
		}

		return Path.IsPathRooted(name) ? name : Path.Combine(Path.GetTempPath(), name);
	}

	private static bool Exists(string endpoint) =>
		OperatingSystem.IsWindows() ? ListEndpoints().Contains(endpoint, StringComparer.OrdinalIgnoreCase) : File.Exists(endpoint);

	// Without the \\.\pipe\ prefix, as NamedPipeClientStream wants them.
	private static List<string> ListEndpoints()
	{
		try
		{
			if (OperatingSystem.IsWindows())
			{
				return [.. Directory.EnumerateFiles(PipePrefix).Select(path => path.StartsWith(PipePrefix, StringComparison.OrdinalIgnoreCase) ? path[PipePrefix.Length..] : path)];
			}

			return [.. new[] { Path.GetTempPath(), "/tmp" }
				.Where(Directory.Exists)
				.Distinct(StringComparer.Ordinal)
				.SelectMany(directory => Directory.EnumerateFileSystemEntries(directory))];
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return [];
		}
	}
}
