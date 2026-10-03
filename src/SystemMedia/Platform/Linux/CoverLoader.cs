using MacroDeck.Sdk.MusicPlayer;
using SystemMedia.Core;

namespace SystemMedia.Platform.Linux;

// An MPRIS artUrl: a file:// or web address, or a data: URI.
internal static class CoverLoader
{
	internal const int MaxBytes = 8 * 1024 * 1024;

	internal static async Task<MusicPlayerArtwork?> LoadAsync(string artUrl, HttpClient http, CancellationToken cancellationToken)
	{
		try
		{
			if (artUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
			{
				return FromDataUri(artUrl);
			}

			if (!Uri.TryCreate(artUrl, UriKind.Absolute, out var uri))
			{
				return null;
			}

			if (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
			{
				using var response = await http.GetAsync(uri, cancellationToken);
				if (!response.IsSuccessStatusCode)
				{
					return null;
				}

				var downloaded = await response.Content.ReadAsByteArrayAsync(cancellationToken);
				return Artwork(downloaded, response.Content.Headers.ContentType?.MediaType);
			}

			return uri.IsFile && File.Exists(uri.LocalPath) && new FileInfo(uri.LocalPath).Length <= MaxBytes
				? Artwork(await File.ReadAllBytesAsync(uri.LocalPath, cancellationToken), reported: null)
				: null;
		}
		catch (Exception exception) when (exception is HttpRequestException or IOException or UnauthorizedAccessException or FormatException or ArgumentException or NotSupportedException ||
			(exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
		{
			return null;
		}
	}

	private static MusicPlayerArtwork? FromDataUri(string artUrl)
	{
		var comma = artUrl.IndexOf(',', StringComparison.Ordinal);
		if (comma < 0 || !artUrl.AsSpan(0, comma).EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		if (artUrl.Length - comma > MaxBytes / 3 * 4 + 4)
		{
			return null;
		}

		var mediaType = artUrl[5..comma].Split(';')[0];
		return Artwork(Convert.FromBase64String(artUrl[(comma + 1)..]), mediaType);
	}

	// Only real image data: the address comes from the player, so anything else is never passed on.
	private static MusicPlayerArtwork? Artwork(byte[] bytes, string? reported) =>
		bytes.Length is > 0 and <= MaxBytes && ArtworkFormat.IsImage(bytes)
			? new MusicPlayerArtwork(bytes, ArtworkFormat.ContentTypeOrSniffed(reported?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true ? reported : null, bytes))
			: null;
}
