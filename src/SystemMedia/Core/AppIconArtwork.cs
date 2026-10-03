using System.Security.Cryptography;
using System.Text;

namespace SystemMedia.Core;

internal static class AppIconArtwork
{
	private const string Prefix = "icon-";

	internal static string IdFor(string appId) =>
		Prefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(appId)).AsSpan(0, 8));

	internal static bool IsIconId(string artworkId) => artworkId.StartsWith(Prefix, StringComparison.Ordinal);
}
