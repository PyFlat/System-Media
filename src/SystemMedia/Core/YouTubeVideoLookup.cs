using System.Net.Http.Json;
using System.Text.Json.Serialization;
using MacroDeck.Sdk.MusicPlayer;

namespace SystemMedia.Core;

internal sealed record YouTubeVideo(string Id, string Title, string? Channel, MusicPlayerArtwork? Thumbnail);

internal sealed class YouTubeVideoLookup : IDisposable
{
	private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(5);

	// The 1280x720 thumbnail does not exist for every video; the 320x180 one always does.
	private static readonly string[] _thumbnailNames = ["hq720.jpg", "mqdefault.jpg"];

	private readonly HttpClient _http = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) })
	{
		Timeout = _timeout,
		MaxResponseContentBufferSize = 8 * 1024 * 1024,
	};

	internal async Task<YouTubeVideo?> LookUpAsync(string videoId, CancellationToken cancellationToken)
	{
		var watchUrl = Uri.EscapeDataString("https://www.youtube.com/watch?v=" + videoId);
		OEmbed? oEmbed;
		try
		{
			oEmbed = await _http.GetFromJsonAsync<OEmbed>($"https://www.youtube.com/oembed?format=json&url={watchUrl}", cancellationToken);
		}
		catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException &&
			!cancellationToken.IsCancellationRequested)
		{
			return null;
		}

		if (oEmbed is not { Title: { Length: > 0 } title })
		{
			return null;
		}

		return new YouTubeVideo(videoId, title, oEmbed.AuthorName, await ReadThumbnailAsync(videoId, cancellationToken));
	}

	public void Dispose() => _http.Dispose();

	private async Task<MusicPlayerArtwork?> ReadThumbnailAsync(string videoId, CancellationToken cancellationToken)
	{
		foreach (var name in _thumbnailNames)
		{
			try
			{
				using var response = await _http.GetAsync($"https://i.ytimg.com/vi/{videoId}/{name}", cancellationToken);
				if (response.IsSuccessStatusCode)
				{
					var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
					if (ArtworkFormat.IsImage(bytes))
					{
						return new MusicPlayerArtwork(bytes, ArtworkFormat.ContentTypeOrSniffed(response.Content.Headers.ContentType?.MediaType, bytes));
					}
				}
			}
			catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
			{
				return null;
			}
		}

		return null;
	}

	private sealed record OEmbed(
		[property: JsonPropertyName("title")] string? Title,
		[property: JsonPropertyName("author_name")] string? AuthorName);
}
