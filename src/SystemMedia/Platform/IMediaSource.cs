namespace SystemMedia.Platform;

internal interface IMediaSource : IMediaPlatform
{
	// Ids must never collide with the operating system's.
	bool Owns(string appId);
}
