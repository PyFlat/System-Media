namespace SystemMedia.Platform.Linux;

// The MPRIS instance id changes with every start, so a web app or second profile is named by its launch arguments.
internal static class LaunchVariant
{
	// The variant ends up in the app id, its stored name and the instance list.
	private const int MaxLength = 100;

	internal static string? Of(IReadOnlyList<string> arguments, string appId)
	{
		var variant = Value(arguments, "class") ?? Value(arguments, "name") ?? Last(Value(arguments, "profile"))
			?? Value(arguments, "P") ?? Last(Value(arguments, "user-data-dir"));
		return variant is { Length: > 0 and <= MaxLength } && !variant.Any(char.IsControl) &&
			!string.Equals(variant, appId, StringComparison.OrdinalIgnoreCase) ? variant : null;
	}

	internal static IReadOnlyList<string> ReadArguments(uint processId)
	{
		try
		{
			return File.ReadAllText($"/proc/{processId}/cmdline").TrimEnd('\0').Split('\0');
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return [];
		}
	}

	// Firefox takes options with one dash or two, followed by the value or joined to it with "=".
	private static string? Value(IReadOnlyList<string> arguments, string option)
	{
		for (var index = 1; index < arguments.Count; index++)
		{
			var argument = arguments[index];
			var name = argument.StartsWith("--", StringComparison.Ordinal) ? argument[2..] : argument.StartsWith('-') ? argument[1..] : null;
			if (name is null)
			{
				continue;
			}

			if (string.Equals(name, option, StringComparison.Ordinal))
			{
				return index + 1 < arguments.Count && !arguments[index + 1].StartsWith('-') ? arguments[index + 1].Trim() : null;
			}

			if (name.StartsWith(option + "=", StringComparison.Ordinal))
			{
				return name[(option.Length + 1)..].Trim();
			}
		}

		return null;
	}

	private static string? Last(string? path) => path is null ? null : Path.GetFileName(path.TrimEnd('/'));
}
