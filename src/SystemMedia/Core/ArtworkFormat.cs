namespace SystemMedia.Core;

internal static class ArtworkFormat
{
	internal static string SniffContentType(ReadOnlySpan<byte> bytes) => bytes switch
	{
		[0x89, 0x50, 0x4E, 0x47, ..] => "image/png",
		[0xFF, 0xD8, 0xFF, ..] => "image/jpeg",
		[0x47, 0x49, 0x46, 0x38, ..] => "image/gif",
		[0x42, 0x4D, ..] => "image/bmp",
		[0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..] => "image/webp",
		_ => "application/octet-stream",
	};

	internal static bool IsImage(ReadOnlySpan<byte> bytes) => SniffContentType(bytes) != "application/octet-stream";

	internal static string ContentTypeOrSniffed(string? reported, ReadOnlySpan<byte> bytes) =>
		string.IsNullOrWhiteSpace(reported) ? SniffContentType(bytes) : reported;
}
