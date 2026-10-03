using System.Security.Cryptography;
using System.Text;
using MacroDeck.Sdk.MusicPlayer;

namespace SystemMedia.Core;

internal static class MediaStateMapper
{
	// An app that never reported a timeline leaves this at the FILETIME epoch.
	private static readonly DateTimeOffset _earliestRealUpdate = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

	// Connected, not disconnected: having no session does not mean the provider went away.
	internal static MusicPlayerState Idle(string? appName = null) =>
		new() { IsConnected = true, PlaybackState = PlaybackState.Stopped, DeviceName = appName };

	internal static MusicPlayerState ToState(MediaSnapshot snapshot, DateTimeOffset now, int? volumePercent)
	{
		var duration = Duration(snapshot);
		return new MusicPlayerState
		{
			IsConnected = true,
			PlaybackState = snapshot.Status,
			TrackName = NullIfBlank(snapshot.Title),
			Artists = (NullIfBlank(snapshot.Artist) ?? NullIfBlank(snapshot.AlbumArtist)) is { } artist ? [artist] : [],
			AlbumName = NullIfBlank(snapshot.AlbumTitle),
			ArtworkId = snapshot.HasThumbnail ? ArtworkId(snapshot) : null,
			Position = Position(snapshot, duration, now),
			Duration = duration,
			VolumePercent = volumePercent,
			ShuffleEnabled = snapshot.IsShuffleActive ?? false,
			RepeatMode = snapshot.RepeatMode ?? RepeatMode.Off,
			DeviceName = snapshot.AppName,
		};
	}

	// Never a URL: the host resolves it back through GetArtworkAsync.
	internal static string ArtworkId(MediaSnapshot snapshot)
	{
		var hash = SHA256.HashData(Encoding.UTF8.GetBytes(snapshot.TrackKey));
		return "art-" + Convert.ToHexStringLower(hash.AsSpan(0, 8));
	}

	private static TimeSpan? Duration(MediaSnapshot snapshot)
	{
		var duration = snapshot.EndTime - snapshot.StartTime;
		return duration > TimeSpan.Zero ? duration : null;
	}

	// Apps report their position only now and then, so while playing it is projected forward from the stamp.
	private static TimeSpan? Position(MediaSnapshot snapshot, TimeSpan? duration, DateTimeOffset now)
	{
		if (duration is null && snapshot.Position <= TimeSpan.Zero)
		{
			return null;
		}

		var position = snapshot.Position - snapshot.StartTime;
		if (snapshot.Status == PlaybackState.Playing &&
			snapshot.LastUpdatedTime > _earliestRealUpdate &&
			now > snapshot.LastUpdatedTime)
		{
			var rate = snapshot.PlaybackRate is { } reported && reported > 0 ? reported : 1d;
			position += (now - snapshot.LastUpdatedTime) * rate;
		}

		if (position < TimeSpan.Zero)
		{
			return TimeSpan.Zero;
		}

		return duration is { } length && position > length ? length : position;
	}

	private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
