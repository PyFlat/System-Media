using System.Security.Cryptography;
using System.Text;

namespace SystemMedia.Core;

// Instance ids must be local ids, while app ids are free-form. Widgets persist these, so the mapping must
// never change; the hash keeps two apps from colliding.
internal static class InstanceIds
{
	internal const string Current = "current";

	private const string AppPrefix = "app-";
	private const int MaxSlugLength = 40;

	internal static string ForApp(string appUserModelId)
	{
		var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(appUserModelId)).AsSpan(0, 4));
		var slug = Slug(appUserModelId);
		return slug.Length == 0 ? AppPrefix + hash : $"{AppPrefix}{slug}-{hash}";
	}

	private static string Slug(string value)
	{
		var builder = new StringBuilder(Math.Min(value.Length, MaxSlugLength));
		foreach (var character in value)
		{
			if (builder.Length >= MaxSlugLength)
			{
				break;
			}

			var lower = char.ToLowerInvariant(character);
			if (lower is >= 'a' and <= 'z' or >= '0' and <= '9')
			{
				builder.Append(lower);
			}
			else if (builder.Length > 0 && builder[^1] != '-')
			{
				builder.Append('-');
			}
		}

		return builder.ToString().TrimEnd('-');
	}
}
