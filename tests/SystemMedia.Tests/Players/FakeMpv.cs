using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SystemMedia.Tests.Players;

internal sealed class FakeMpv : IAsyncDisposable
{
	internal static readonly byte[] Cover = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 7];

	private readonly FakeLocalServer _server;

	internal FakeMpv(string name)
	{
		_server = new FakeLocalServer(name, ServeAsync);
	}

	internal string Endpoint => _server.Endpoint;

	internal int Connections => _server.Connections;

	internal ConcurrentDictionary<string, JsonNode?> Properties { get; } = new(StringComparer.Ordinal)
	{
		["idle-active"] = false,
		["media-title"] = "song.mp3",
		["metadata"] = new JsonObject { ["TITLE"] = "Numb", ["Artist"] = "Linkin Park", ["album"] = "Meteora" },
		["pause"] = false,
		["time-pos"] = 12.5,
		["duration"] = 185.0,
		["volume"] = 80.0,
		["current-tracks/video"] = new JsonObject { ["albumart"] = true },
	};

	internal ConcurrentQueue<string> Commands { get; } = new();

	public ValueTask DisposeAsync() => _server.DisposeAsync();

	private async Task ServeAsync(Stream stream, CancellationToken cancellationToken)
	{
		using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
		while (await reader.ReadLineAsync(cancellationToken) is { } line)
		{
			var request = JsonNode.Parse(line)!;
			var command = request["command"]!.AsArray().Select(part => part!.ToJsonString().Trim('"')).ToArray();
			var reply = new JsonObject { ["request_id"] = request["request_id"]!.GetValue<long>(), ["error"] = "success" };
			switch (command[0])
			{
				case "get_property":
					if (Properties.TryGetValue(command[1], out var value))
					{
						reply["data"] = value?.DeepClone();
					}
					else
					{
						reply["error"] = "property unavailable";
					}

					break;
				case "screenshot-to-file":
					Commands.Enqueue("screenshot-to-file " + command[2]);
					await File.WriteAllBytesAsync(JsonSerializer.Deserialize<string>(request["command"]![1]!.ToJsonString())!, Cover, cancellationToken);
					break;
				case "playlist-next":
					Commands.Enqueue(command[0]);
					reply["error"] = "error running command";
					break;
				default:
					Commands.Enqueue(string.Join(' ', command));
					break;
			}

			await stream.WriteAsync(Encoding.UTF8.GetBytes(reply.ToJsonString() + "\n"), cancellationToken);
			await stream.FlushAsync(cancellationToken);
		}
	}
}
