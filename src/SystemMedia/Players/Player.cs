using MacroDeck.Sdk.MusicPlayer;

namespace SystemMedia.Players;

internal enum PlayerCommand
{
	Play,
	Pause,
	Toggle,
	Next,
	Previous,
	Seek,
	Volume,
}

internal sealed record PlayerReading(
	PlaybackState State,
	string? Title,
	string? Artist = null,
	string? Album = null,
	TimeSpan? Position = null,
	TimeSpan? Duration = null,
	int? VolumePercent = null,
	bool HasCover = false)
{
	internal string TrackKey => string.Join('\u001f', Title, Artist, Album);
}

// A player the system's media controls do not see. See docs/adding-a-player.md.
internal abstract class Player : IAsyncDisposable
{
	internal const string AppIdPrefix = "player:";

	// Part of the instance id widgets persist, so it must never change.
	internal abstract string Id { get; }

	internal abstract string Name { get; }

	// Without ".exe". Finds the app's icon and, on Windows, its volume mixer entry.
	internal virtual string? ProcessName => null;

	internal virtual TimeSpan Interval => TimeSpan.FromSeconds(1);

	internal string AppId => AppIdPrefix + Id;

	// null while the player is not running or has nothing loaded.
	internal abstract Task<PlayerReading?> ReadAsync(CancellationToken cancellationToken);

	internal virtual Task<MusicPlayerArtwork?> ReadCoverAsync(CancellationToken cancellationToken) =>
		Task.FromResult<MusicPlayerArtwork?>(null);

	internal virtual bool Supports(PlayerCommand command) => false;

	// value is the position in seconds for Seek and 0 to 100 for Volume.
	internal virtual Task<bool> SendAsync(PlayerCommand command, double? value, CancellationToken cancellationToken) =>
		Task.FromResult(false);

	public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
