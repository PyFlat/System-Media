using System.Diagnostics;
using System.Text.Json;

namespace SystemMedia.Platform.Linux;

// PulseAudio and PipeWire (through pipewire-pulse) both answer pactl, which ships with either. Its JSON output
// (PulseAudio 16 and later) is read rather than the text, which is translated. A device is a sink or source,
// identified by its name.
internal sealed class PulseAudioDevices : IAudioDevices
{
	private static readonly TimeSpan _commandTimeout = TimeSpan.FromSeconds(3);

	// Every variable asks on its own refresh; one reading serves them all.
	private static readonly TimeSpan _cacheDuration = TimeSpan.FromSeconds(1);

	private readonly Lock _gate = new();
	private Task<Snapshot?>? _snapshot;
	private long _snapshotTakenAt;

	public async Task<IReadOnlyList<AudioDevice>> GetDevicesAsync(bool input, CancellationToken cancellationToken) =>
		await GetSnapshotAsync() is { } snapshot ? (input ? snapshot.Sources : snapshot.Sinks) : [];

	// Linux has no separate defaults for calls.
	public async Task<AudioDevice?> GetDefaultAsync(AudioDeviceRole role, CancellationToken cancellationToken)
	{
		if (await GetSnapshotAsync() is not { } snapshot)
		{
			return null;
		}

		var (devices, name) = role.IsInput() ? (snapshot.Sources, snapshot.DefaultSource) : (snapshot.Sinks, snapshot.DefaultSink);
		return devices.FirstOrDefault(device => string.Equals(device.Id, name, StringComparison.Ordinal));
	}

	public async Task<bool> SetDefaultAsync(AudioDeviceRole role, string deviceId, CancellationToken cancellationToken)
	{
		if (!(await GetDevicesAsync(role.IsInput(), cancellationToken)).Any(device => string.Equals(device.Id, deviceId, StringComparison.Ordinal)))
		{
			return false;
		}

		var command = role.IsInput() ? "set-default-source" : "set-default-sink";
		_ = await RunAsync(cancellationToken, command, deviceId)
			?? throw new InvalidOperationException($"pactl {command} failed or pactl is not installed.");
		lock (_gate)
		{
			_snapshot = null;
		}

		return true;
	}

	internal static IReadOnlyList<AudioDevice> ParseDevices(string json)
	{
		using var document = JsonDocument.Parse(json);
		var devices = new List<AudioDevice>();
		foreach (var element in document.RootElement.EnumerateArray())
		{
			// A sink's monitor is a source too, but nobody records from it by choice.
			if (Text(element, "name") is not { } name || Text(element, "description") is not { } description ||
				Text(element, "monitor_of_sink") is { } monitored && monitored != "n/a" ||
				name.EndsWith(".monitor", StringComparison.Ordinal))
			{
				continue;
			}

			devices.Add(new AudioDevice(name, description));
		}

		return devices;
	}

	internal static (string? Sink, string? Source) ParseDefaults(string json)
	{
		using var document = JsonDocument.Parse(json);
		return (Text(document.RootElement, "default_sink_name"), Text(document.RootElement, "default_source_name"));
	}

	private Task<Snapshot?> GetSnapshotAsync()
	{
		lock (_gate)
		{
			if (_snapshot is null || Stopwatch.GetElapsedTime(_snapshotTakenAt) > _cacheDuration)
			{
				_snapshot = ReadSnapshotAsync();
				_snapshotTakenAt = Stopwatch.GetTimestamp();
			}

			return _snapshot;
		}
	}

	// Not cancelled by a caller: the reading is shared.
	private static async Task<Snapshot?> ReadSnapshotAsync()
	{
		try
		{
			var info = RunAsync(CancellationToken.None, "info");
			var sinks = RunAsync(CancellationToken.None, "list", "sinks");
			var sources = RunAsync(CancellationToken.None, "list", "sources");
			if (await info is not { } infoJson || await sinks is not { } sinksJson || await sources is not { } sourcesJson)
			{
				return null;
			}

			var (defaultSink, defaultSource) = ParseDefaults(infoJson);
			return new Snapshot(ParseDevices(sinksJson), ParseDevices(sourcesJson), defaultSink, defaultSource);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	// null when pactl is missing, too old for JSON, or has no sound server to talk to.
	private static async Task<string?> RunAsync(CancellationToken cancellationToken, params string[] arguments)
	{
		if (Locate() is not { } executable)
		{
			return null;
		}

		var start = new ProcessStartInfo(executable)
		{
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true,
		};
		start.ArgumentList.Add("--format=json");
		foreach (var argument in arguments)
		{
			start.ArgumentList.Add(argument);
		}

		// Some locales put decimal commas into the JSON numbers.
		start.Environment.Remove("LC_ALL");
		start.Environment["LC_NUMERIC"] = "C";

		using var process = Process.Start(start);
		if (process is null)
		{
			return null;
		}

		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(_commandTimeout);
		try
		{
			var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
			_ = process.StandardError.ReadToEndAsync(timeout.Token);
			await process.WaitForExitAsync(timeout.Token);
			var text = await output;
			return process.ExitCode == 0 ? text : null;
		}
		catch (OperationCanceledException)
		{
			process.Kill(entireProcessTree: true);
			if (cancellationToken.IsCancellationRequested)
			{
				throw;
			}

			return null;
		}
	}

	// A process started outside a login shell may have a bare PATH, so the usual folder is checked too.
	private static string? Locate() =>
		(Environment.GetEnvironmentVariable("PATH") ?? "")
			.Split(':', StringSplitOptions.RemoveEmptyEntries)
			.Append("/usr/bin")
			.Select(folder => Path.Combine(folder, "pactl"))
			.FirstOrDefault(File.Exists);

	private static string? Text(JsonElement element, string property) =>
		element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;

	private sealed record Snapshot(IReadOnlyList<AudioDevice> Sinks, IReadOnlyList<AudioDevice> Sources, string? DefaultSink, string? DefaultSource);
}
