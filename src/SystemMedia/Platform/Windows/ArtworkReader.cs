using MacroDeck.Sdk.MusicPlayer;
using SystemMedia.Core;
using Windows.Storage.Streams;

namespace SystemMedia.Platform.Windows;

internal static class ArtworkReader
{
	internal static async Task<MusicPlayerArtwork?> ReadAsync(IRandomAccessStreamReference thumbnail, CancellationToken cancellationToken)
	{
		using var stream = await thumbnail.OpenReadAsync().OnThreadPool(cancellationToken);
		var reportedType = stream.ContentType;
		await using var source = stream.AsStreamForRead();
		using var buffer = new MemoryStream();
		await source.CopyToAsync(buffer, cancellationToken).OnThreadPool();
		if (buffer.Length == 0)
		{
			return null;
		}

		var bytes = buffer.ToArray();
		var contentType = ArtworkFormat.ContentTypeOrSniffed(reportedType, bytes);
		return new MusicPlayerArtwork(bytes, contentType);
	}

}
