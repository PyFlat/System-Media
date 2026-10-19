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
	// A variable id may only be 64 characters long, including its prefix and the longest variable name.
	private const int MaxVariableSlugLength = 22;

	internal static string ForApp(string appUserModelId)
	{
		var hash = Hash(appUserModelId);
		var slug = Slug(appUserModelId, MaxSlugLength);
		return slug.Length == 0 ? AppPrefix + hash : $"{AppPrefix}{slug}-{hash}";
	}

	// The app's part of its variables' ids. Templates persist the names, so this mapping must never change either.
	internal static string ForAppVariables(string appUserModelId)
	{
		var hash = Hash(appUserModelId);
		var slug = Slug(appUserModelId, MaxVariableSlugLength);
		return slug.Length == 0 ? hash : $"{slug}-{hash}";
	}

	private static string Hash(string value) =>
		Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 4));

	private static string Slug(string value, int maxLength)
	{
		var builder = new StringBuilder(Math.Min(value.Length, maxLength));
		foreach (var character in value)
		{
			if (builder.Length >= maxLength)
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
