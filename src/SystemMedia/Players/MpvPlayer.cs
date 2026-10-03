using System.Globalization;
using System.Text.Json;
using MacroDeck.Sdk.MusicPlayer;
using SystemMedia.Core;
using SystemMedia.Players.Mpv;

namespace SystemMedia.Players;

// Plain mpv, once its IPC server is on: input-ipc-server=mpvsocket in mpv.conf (/tmp/mpvsocket on macOS
// and Linux). mpv shows embedded cover art as a video track, so a screenshot of it is the cover.
internal class MpvPlayer(IReadOnlyList<string> sockets) : Player
{
	// A video's first frames are often black.
	private static readonly TimeSpan _videoThumbnailAfter = TimeSpan.FromSeconds(3);

	private readonly SemaphoreSlim _connectGate = new(1, 1);

	private MpvIpcClient? _client;
	private string? _endpointSeenBefore;

	internal MpvPlayer()
		: this(["mpvsocket", "/tmp/mpvsocket"])
	{
	}

	internal override string Id => "mpv";

	internal override string Name => "mpv";

	internal override string? ProcessName => "mpv";

	internal override async Task<PlayerReading?> ReadAsync(CancellationToken cancellationToken)
	{
		if (await ConnectedClientAsync(cancellationToken) is not { } client)
		{
			return null;
		}

		try
		{
			var idle = client.GetPropertyAsync("idle-active", cancellationToken);
			var title = client.GetPropertyAsync("media-title", cancellationToken);
			var metadata = client.GetPropertyAsync("metadata", cancellationToken);
			var pause = client.GetPropertyAsync("pause", cancellationToken);
			var position = client.GetPropertyAsync("time-pos", cancellationToken);
			var duration = client.GetPropertyAsync("duration", cancellationToken);
			var volume = client.GetPropertyAsync("volume", cancellationToken);
			var video = client.GetPropertyAsync("current-tracks/video", cancellationToken);
			await Task.WhenAll(idle, title, metadata, pause, position, duration, volume, video);

			if (idle.Result is { ValueKind: JsonValueKind.True } || title.Result is null)
			{
				return null;
			}

			var tags = metadata.Result;
			var positionValue = Seconds(position.Result);
			var durationValue = Seconds(duration.Result);
			return new PlayerReading(
				pause.Result is { ValueKind: JsonValueKind.True } ? PlaybackState.Paused : PlaybackState.Playing,
				Tag(tags, "title") ?? title.Result.Value.GetString(),
				Tag(tags, "artist") ?? Tag(tags, "album_artist"),
				Tag(tags, "album"),
				positionValue,
				durationValue,
				volume.Result is { ValueKind: JsonValueKind.Number } level ? (int)Math.Clamp(Math.Round(level.GetDouble()), 0, 100) : null,
				HasCover: HasThumbnail(video.Result, positionValue, durationValue));
		}
		catch (Exception exception) when (exception is IOException or ObjectDisposedException or TimeoutException)
		{
			await DropClientAsync(client);
			return null;
		}
	}

	internal override async Task<MusicPlayerArtwork?> ReadCoverAsync(CancellationToken cancellationToken)
	{
		if (_client is not { IsConnected: true } client)
		{
			return null;
		}

		var file = Path.Combine(Path.GetTempPath(), $"system-media-cover-{Guid.NewGuid():N}.jpg");
		try
		{
			// IPC commands are no-osd by default, so the player shows nothing.
			await client.SendAsync(["screenshot-to-file", file, "video"], cancellationToken);
			var bytes = await File.ReadAllBytesAsync(file, cancellationToken);
			return ArtworkFormat.IsImage(bytes) ? new MusicPlayerArtwork(bytes, ArtworkFormat.SniffContentType(bytes)) : null;
		}
		catch (Exception exception) when (exception is IOException or MpvCommandException or TimeoutException or UnauthorizedAccessException)
		{
			return null;
		}
		finally
		{
			TryDelete(file);
		}
	}

	internal override bool Supports(PlayerCommand command) => true;

	internal override async Task<bool> SendAsync(PlayerCommand command, double? value, CancellationToken cancellationToken)
	{
		object[]? request = command switch
		{
			PlayerCommand.Play => ["set_property", "pause", false],
			PlayerCommand.Pause => ["set_property", "pause", true],
			PlayerCommand.Toggle => ["cycle", "pause"],
			PlayerCommand.Next => ["playlist-next"],
			PlayerCommand.Previous => ["playlist-prev"],
			PlayerCommand.Seek when value is { } seconds => ["seek", seconds.ToString("0.###", CultureInfo.InvariantCulture), "absolute"],
			PlayerCommand.Volume when value is { } percent => ["set_property", "volume", percent],
			_ => null,
		};
		if (request is null || await ConnectedClientAsync(cancellationToken) is not { } client)
		{
			return false;
		}

		try
		{
			await client.SendAsync(request, cancellationToken);
			return true;
		}
		catch (Exception exception) when (exception is MpvCommandException or IOException or TimeoutException)
		{
			return false;
		}
	}

	public override async ValueTask DisposeAsync()
	{
		if (_client is { } client)
		{
			_client = null;
			await client.DisposeAsync();
		}

		_connectGate.Dispose();
		await base.DisposeAsync();
	}

	// mpv on Windows serves each client on its own pipe instance, and a front end starting mpv must get the first
	// one, so only an endpoint seen on the previous poll is connected to.
	private async Task<MpvIpcClient?> ConnectedClientAsync(CancellationToken cancellationToken)
	{
		if (_client is { IsConnected: true } connected)
		{
			return connected;
		}

		await _connectGate.WaitAsync(cancellationToken);
		try
		{
			if (_client is { IsConnected: true } raced)
			{
				return raced;
			}

			if (_client is { } broken)
			{
				// A front end restarts mpv per file under the same name, so the new server counts as unseen.
				_client = null;
				_endpointSeenBefore = null;
				await broken.DisposeAsync();
				return null;
			}

			var endpoint = LocalSockets.Find(sockets) is [var first, ..] ? first : null;
			var seenBefore = endpoint is not null && string.Equals(endpoint, _endpointSeenBefore, StringComparison.Ordinal);
			_endpointSeenBefore = endpoint;
			if (!seenBefore)
			{
				return null;
			}

			try
			{
				_client = await MpvIpcClient.ConnectAsync(endpoint!, cancellationToken);
				return _client;
			}
			catch (Exception exception) when (exception is IOException or TimeoutException or OperationCanceledException or UnauthorizedAccessException or System.Net.Sockets.SocketException)
			{
				cancellationToken.ThrowIfCancellationRequested();
				return null;
			}
		}
		finally
		{
			_connectGate.Release();
		}
	}

	private async Task DropClientAsync(MpvIpcClient client)
	{
		if (ReferenceEquals(Interlocked.CompareExchange(ref _client, null, client), client))
		{
			_endpointSeenBefore = null;
			await client.DisposeAsync();
		}
	}

	private static bool HasThumbnail(JsonElement? video, TimeSpan? position, TimeSpan? duration)
	{
		if (video is not { ValueKind: JsonValueKind.Object } track)
		{
			return false;
		}

		var isCover = track.TryGetProperty("albumart", out var albumArt) && albumArt.ValueKind == JsonValueKind.True;
		return isCover || position >= _videoThumbnailAfter || duration < _videoThumbnailAfter * 2;
	}

	// ID3 gives title, Vorbis TITLE.
	private static string? Tag(JsonElement? metadata, string name)
	{
		if (metadata is not { ValueKind: JsonValueKind.Object } tags)
		{
			return null;
		}

		foreach (var tag in tags.EnumerateObject())
		{
			if (string.Equals(tag.Name, name, StringComparison.OrdinalIgnoreCase) &&
				tag.Value.ValueKind == JsonValueKind.String && tag.Value.GetString() is { Length: > 0 } value)
			{
				return value;
			}
		}

		return null;
	}

	private static TimeSpan? Seconds(JsonElement? value) =>
		value is { ValueKind: JsonValueKind.Number } number && number.GetDouble() is var seconds && seconds >= 0 && double.IsFinite(seconds)
			? TimeSpan.FromSeconds(seconds)
			: null;

	private static void TryDelete(string file)
	{
		try
		{
			File.Delete(file);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
		}
	}
}
