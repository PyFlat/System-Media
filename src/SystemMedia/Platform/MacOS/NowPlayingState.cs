using System.Text.Json;
using MacroDeck.Sdk.MusicPlayer;
using SystemMedia.Core;

namespace SystemMedia.Platform.MacOS;

internal sealed record NowPlayingInfo
{
	// For a browser tab this is the browser, not the helper process macOS attributes playback to.
	public required string AppId { get; init; }

	public bool Playing { get; init; }

	public string? Title { get; init; }

	public string? Artist { get; init; }

	public string? Album { get; init; }

	public long? DurationMicros { get; init; }

	public long? ElapsedMicros { get; init; }

	public long? TimestampEpochMicros { get; init; }

	public double? PlaybackRate { get; init; }

	public int? ShuffleMode { get; init; }

	public int? RepeatMode { get; init; }

	public string? ArtworkMimeType { get; init; }

	public string? ArtworkBase64 { get; init; }

	internal MediaSnapshot ToSnapshot() => new()
	{
		AppId = AppId,
		AppName = string.Empty,
		Title = Title,
		Artist = Artist,
		AlbumTitle = Album,
		HasThumbnail = !string.IsNullOrEmpty(ArtworkBase64),
		Status = Playing ? PlaybackState.Playing : PlaybackState.Paused,
		PlaybackRate = PlaybackRate,
		// MediaRemote's shuffle and repeat modes: 1 off, 2 albums or one track, 3 tracks or the whole list.
		IsShuffleActive = ShuffleMode is null or 0 ? null : ShuffleMode > 1,
		RepeatMode = RepeatMode switch
		{
			1 => MacroDeck.Sdk.MusicPlayer.RepeatMode.Off,
			2 => MacroDeck.Sdk.MusicPlayer.RepeatMode.Track,
			3 => MacroDeck.Sdk.MusicPlayer.RepeatMode.Context,
			_ => null,
		},
		StartTime = TimeSpan.Zero,
		EndTime = DurationMicros is { } duration ? TimeSpan.FromMicroseconds(duration) : TimeSpan.Zero,
		Position = ElapsedMicros is { } elapsed ? TimeSpan.FromMicroseconds(elapsed) : TimeSpan.Zero,
		LastUpdatedTime = TimestampEpochMicros is { } stamp
			? DateTimeOffset.FromUnixTimeMilliseconds(stamp / 1000)
			: DateTimeOffset.MinValue,
	};

	internal MusicPlayerArtwork? DecodeArtwork()
	{
		if (string.IsNullOrEmpty(ArtworkBase64))
		{
			return null;
		}

		try
		{
			var bytes = Convert.FromBase64String(ArtworkBase64);
			return bytes.Length == 0 ? null : new MusicPlayerArtwork(bytes, ArtworkFormat.ContentTypeOrSniffed(ArtworkMimeType, bytes));
		}
		catch (FormatException)
		{
			return null;
		}
	}
}

// Lines are {"type":"data","diff":bool,"payload":{...}}. A diff updates only the keys it carries and a key
// set to null has vanished. Read with --micros, so times are microseconds.
internal sealed class NowPlayingState
{
	private readonly Dictionary<string, JsonElement> _fields = new(StringComparer.Ordinal);
	private volatile NowPlayingInfo? _current;

	internal NowPlayingInfo? Current => _current;

	internal bool Apply(string line)
	{
		using var document = JsonDocument.Parse(line);
		var root = document.RootElement;
		if (root.ValueKind != JsonValueKind.Object ||
			!root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != "data" ||
			!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
		{
			return false;
		}

		var isDiff = root.TryGetProperty("diff", out var diff) && diff.ValueKind == JsonValueKind.True;
		if (!isDiff)
		{
			_fields.Clear();
		}

		foreach (var property in payload.EnumerateObject())
		{
			if (property.Value.ValueKind == JsonValueKind.Null)
			{
				_fields.Remove(property.Name);
			}
			else
			{
				_fields[property.Name] = property.Value.Clone();
			}
		}

		var previousApp = _current?.AppId;
		_current = Build();
		return !string.Equals(previousApp, _current?.AppId, StringComparison.Ordinal);
	}

	internal void Clear() => _current = null;

	private NowPlayingInfo? Build()
	{
		var app = String("parentApplicationBundleIdentifier") ?? String("bundleIdentifier");
		if (string.IsNullOrEmpty(app))
		{
			return null;
		}

		return new NowPlayingInfo
		{
			AppId = app,
			Playing = _fields.TryGetValue("playing", out var playing) && playing.ValueKind == JsonValueKind.True,
			Title = String("title"),
			Artist = String("artist"),
			Album = String("album"),
			DurationMicros = Integer("durationMicros"),
			ElapsedMicros = Integer("elapsedTimeMicros"),
			TimestampEpochMicros = Integer("timestampEpochMicros"),
			PlaybackRate = Number("playbackRate"),
			ShuffleMode = (int?)Integer("shuffleMode"),
			RepeatMode = (int?)Integer("repeatMode"),
			ArtworkMimeType = String("artworkMimeType"),
			ArtworkBase64 = String("artworkData"),
		};
	}

	private string? String(string key) =>
		_fields.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

	private long? Integer(string key) =>
		_fields.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.Number
			? value.TryGetInt64(out var integer) ? integer : (long)value.GetDouble()
			: null;

	private double? Number(string key) =>
		_fields.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;
}
