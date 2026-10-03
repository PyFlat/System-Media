using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Serilog;

namespace SystemMedia.Core;

internal sealed record ThumbnailSighting(string Track, string? Album, string? Artist)
{
	internal static ThumbnailSighting Of(MediaSnapshot snapshot) =>
		new(Hash(snapshot.TrackKey), HashName(snapshot.AlbumTitle), HashName(snapshot.Artist));

	internal bool IsTitleOnly => Album is null && Artist is null;

	internal bool SharesAlbumOrArtist(ThumbnailSighting other) =>
		(Album is not null && string.Equals(Album, other.Album, StringComparison.Ordinal)) ||
		(Artist is not null && string.Equals(Artist, other.Artist, StringComparison.Ordinal));

	private static string? HashName(string? name) =>
		string.IsNullOrWhiteSpace(name) ? null : Hash(name.Trim().ToLowerInvariant());

	private static string Hash(string text) =>
		Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)).AsSpan(0, 12));
}

// Kept across restarts: a browser can hand back a cover from hours ago. Only hashes are stored.
internal sealed class ThumbnailHistory(string? dataDirectory, ILogger logger)
{
	internal const int PerApp = 256;

	private const string FileName = "thumbnail-history.json";

	private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

	private readonly string? _path = string.IsNullOrWhiteSpace(dataDirectory) ? null : Path.Combine(dataDirectory, FileName);
	private readonly Lock _gate = new();

	private Dictionary<string, List<Entry>>? _apps;

	internal static ThumbnailHistory InMemory() => new(null, Serilog.Core.Logger.None);

	// A sighting on a title-only page (usually a leftover) gives way to the first track naming an artist or album.
	internal ThumbnailSighting FirstSeen(string appId, string thumbnailHash, MediaSnapshot snapshot)
	{
		var sighting = ThumbnailSighting.Of(snapshot);
		lock (_gate)
		{
			var apps = _apps ??= Load();
			if (!apps.TryGetValue(appId, out var entries))
			{
				entries = apps[appId] = [];
			}

			var index = entries.FindIndex(entry => string.Equals(entry.Thumbnail, thumbnailHash, StringComparison.Ordinal));
			if (index >= 0 && !(entries[index].Sighting.IsTitleOnly && !sighting.IsTitleOnly))
			{
				return entries[index].Sighting;
			}

			if (index >= 0)
			{
				entries.RemoveAt(index);
			}
			else if (entries.Count >= PerApp)
			{
				entries.RemoveAt(0);
			}

			entries.Add(new Entry(thumbnailHash, sighting));
			Save(apps);
			return sighting;
		}
	}

	private Dictionary<string, List<Entry>> Load()
	{
		if (_path is null || !File.Exists(_path))
		{
			return new(StringComparer.Ordinal);
		}

		try
		{
			var stored = JsonSerializer.Deserialize<Dictionary<string, List<Entry>>>(File.ReadAllText(_path), _json);
			return stored is null
				? new(StringComparer.Ordinal)
				: new(stored.Where(app => app.Value is not null), StringComparer.Ordinal);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
		{
			logger.Warning(exception, "Could not read the thumbnail history from {Path}; starting with none.", _path);
			return new(StringComparer.Ordinal);
		}
	}

	private void Save(Dictionary<string, List<Entry>> apps)
	{
		if (_path is null)
		{
			return;
		}

		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
			File.WriteAllText(_path, JsonSerializer.Serialize(apps, _json));
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			logger.Warning(exception, "Could not save the thumbnail history to {Path}.", _path);
		}
	}

	private sealed record Entry(string Thumbnail, ThumbnailSighting Sighting);
}
