using MacroDeck.Sdk.MusicPlayer;

namespace SystemMedia.Core;

internal sealed record MediaSnapshot
{
	public required string AppId { get; init; }

	public required string AppName { get; init; }

	public string? Title { get; init; }

	public string? Artist { get; init; }

	public string? AlbumArtist { get; init; }

	public string? AlbumTitle { get; init; }

	public bool HasThumbnail { get; init; }

	public PlaybackState Status { get; init; }

	public double? PlaybackRate { get; init; }

	public bool? IsShuffleActive { get; init; }

	public RepeatMode? RepeatMode { get; init; }

	public TimeSpan StartTime { get; init; }

	public TimeSpan EndTime { get; init; }

	public TimeSpan Position { get; init; }

	public DateTimeOffset LastUpdatedTime { get; init; }

	public string TrackKey => string.Join('\u001f', AppId, Title, Artist, AlbumTitle);

	public bool HasDuration => EndTime > StartTime;
}
