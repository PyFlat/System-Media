namespace SystemMedia.Core;

internal sealed record AppIdentity(string AppId, string DisplayName, string? ExecutablePath, string? PackageFamilyName)
{
	internal bool IsFallback { get; init; }

	// Compared by file name: a Start menu shortcut and the running process can disagree on the install folder.
	internal string? ExecutableFileName =>
		ExecutablePath is { Length: > 0 } path ? FileNameOf(path)
		: AppId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? FileNameOf(AppId)
		: null;

	internal static AppIdentity Fallback(string appUserModelId)
	{
		var name = appUserModelId;
		var separator = name.LastIndexOfAny(['\\', '/', '!']);
		if (separator >= 0 && separator < name.Length - 1)
		{
			name = name[(separator + 1)..];
		}

		if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
		{
			name = name[..^4];
		}

		return new AppIdentity(appUserModelId, name, ExecutablePath: null, PackageFamilyNameOf(appUserModelId)) { IsFallback = true };
	}

	// Path.GetFileName only knows the running OS's separator.
	private static string FileNameOf(string path) => path[(path.LastIndexOfAny(['\\', '/']) + 1)..];

	internal static string? PackageFamilyNameOf(string appUserModelId)
	{
		var separator = appUserModelId.IndexOf('!', StringComparison.Ordinal);
		return separator > 0 ? appUserModelId[..separator] : null;
	}
}
