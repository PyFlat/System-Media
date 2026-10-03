namespace SystemMedia.Core;

internal sealed class MediaSessionNotFoundException(string? appName, Exception? innerException = null)
	: InvalidOperationException(
		appName is null ? "No app is currently playing media." : $"{appName} is not playing media right now.",
		innerException);

internal sealed class MediaCommandRejectedException(string appName, string command)
	: InvalidOperationException($"{appName} did not accept the {command} command.");
