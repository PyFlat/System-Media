using MacroDeck.Sdk.MusicPlayer;
using NUnit.Framework;
using SystemMedia.Core;

namespace SystemMedia.Tests;

[TestFixture]
public sealed class MediaStateMapperTests
{
	private static readonly DateTimeOffset _reportedAt = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
	private static readonly string[] _theArtist = ["The Artist"];
	private static readonly string[] _albumArtist = ["Album Artist"];

	[Test]
	public void A_playing_session_maps_its_metadata()
	{
		var state = MediaStateMapper.ToState(Snapshot(), _reportedAt, volumePercent: 40);

		Assert.That(state.IsConnected, Is.True);
		Assert.That(state.PlaybackState, Is.EqualTo(PlaybackState.Playing));
		Assert.That(state.TrackName, Is.EqualTo("Night Transit"));
		Assert.That(state.Artists, Is.EqualTo(_theArtist));
		Assert.That(state.AlbumName, Is.EqualTo("Test Album"));
		Assert.That(state.DeviceName, Is.EqualTo("Spotify"));
		Assert.That(state.VolumePercent, Is.EqualTo(40));
		Assert.That(state.Duration, Is.EqualTo(TimeSpan.FromMinutes(3)));
	}

	[Test]
	public void While_playing_the_position_is_projected_forward_from_when_the_app_reported_it()
	{
		var state = MediaStateMapper.ToState(Snapshot(), _reportedAt.AddSeconds(5), volumePercent: null);

		Assert.That(state.Position, Is.EqualTo(TimeSpan.FromSeconds(35)));
	}

	[Test]
	public void The_projection_follows_the_playback_rate()
	{
		var state = MediaStateMapper.ToState(Snapshot() with { PlaybackRate = 2 }, _reportedAt.AddSeconds(5), volumePercent: null);

		Assert.That(state.Position, Is.EqualTo(TimeSpan.FromSeconds(40)));
	}

	[Test]
	public void While_paused_the_position_stays_where_the_app_reported_it()
	{
		var snapshot = Snapshot() with { Status = PlaybackState.Paused };

		var state = MediaStateMapper.ToState(snapshot, _reportedAt.AddMinutes(10), volumePercent: null);

		Assert.That(state.PlaybackState, Is.EqualTo(PlaybackState.Paused));
		Assert.That(state.Position, Is.EqualTo(TimeSpan.FromSeconds(30)));
	}

	[Test]
	public void The_projected_position_never_runs_past_the_end_of_the_track()
	{
		var state = MediaStateMapper.ToState(Snapshot(), _reportedAt.AddHours(1), volumePercent: null);

		Assert.That(state.Position, Is.EqualTo(TimeSpan.FromMinutes(3)));
	}

	[Test]
	public void An_app_that_never_stamped_its_timeline_is_not_projected()
	{
		var snapshot = Snapshot() with { LastUpdatedTime = DateTimeOffset.FromFileTime(0) };

		var state = MediaStateMapper.ToState(snapshot, _reportedAt, volumePercent: null);

		Assert.That(state.Position, Is.EqualTo(TimeSpan.FromSeconds(30)));
	}

	[Test]
	public void A_timeline_that_does_not_start_at_zero_is_reported_relative_to_its_start()
	{
		var snapshot = Snapshot() with
		{
			StartTime = TimeSpan.FromSeconds(10),
			EndTime = TimeSpan.FromSeconds(70),
			Position = TimeSpan.FromSeconds(25),
			Status = PlaybackState.Paused,
		};

		var state = MediaStateMapper.ToState(snapshot, _reportedAt, volumePercent: null);

		Assert.That(state.Position, Is.EqualTo(TimeSpan.FromSeconds(15)));
		Assert.That(state.Duration, Is.EqualTo(TimeSpan.FromMinutes(1)));
	}

	[Test]
	public void An_app_without_a_timeline_reports_neither_position_nor_duration()
	{
		var snapshot = Snapshot() with { StartTime = TimeSpan.Zero, EndTime = TimeSpan.Zero, Position = TimeSpan.Zero };

		var state = MediaStateMapper.ToState(snapshot, _reportedAt, volumePercent: null);

		Assert.That(state.Position, Is.Null);
		Assert.That(state.Duration, Is.Null);
	}

	[Test]
	public void The_album_artist_stands_in_when_the_track_has_no_artist()
	{
		var state = MediaStateMapper.ToState(Snapshot() with { Artist = " ", AlbumArtist = "Album Artist" }, _reportedAt, null);

		Assert.That(state.Artists, Is.EqualTo(_albumArtist));
	}

	[Test]
	public void Blank_metadata_is_reported_as_missing()
	{
		var state = MediaStateMapper.ToState(Snapshot() with { Title = "", Artist = null, AlbumTitle = "  " }, _reportedAt, null);

		Assert.That(state.TrackName, Is.Null);
		Assert.That(state.Artists, Is.Empty);
		Assert.That(state.AlbumName, Is.Null);
	}

	[Test]
	public void An_app_that_reports_no_repeat_mode_is_shown_as_not_repeating()
	{
		var state = MediaStateMapper.ToState(Snapshot() with { RepeatMode = null }, _reportedAt, null);

		Assert.That(state.RepeatMode, Is.EqualTo(RepeatMode.Off));
	}

	[Test]
	public void Artwork_is_only_offered_when_the_app_attached_a_thumbnail()
	{
		Assert.That(MediaStateMapper.ToState(Snapshot() with { HasThumbnail = false }, _reportedAt, null).ArtworkId, Is.Null);
		Assert.That(MediaStateMapper.ToState(Snapshot(), _reportedAt, null).ArtworkId, Is.Not.Null);
	}

	[Test]
	public void The_artwork_id_changes_with_the_track_and_only_with_the_track()
	{
		var first = MediaStateMapper.ArtworkId(Snapshot());
		var laterInSameTrack = MediaStateMapper.ArtworkId(Snapshot() with { Position = TimeSpan.FromMinutes(2) });
		var nextTrack = MediaStateMapper.ArtworkId(Snapshot() with { Title = "Another Song" });

		Assert.That(laterInSameTrack, Is.EqualTo(first));
		Assert.That(nextTrack, Is.Not.EqualTo(first));
	}

	[Test]
	public void Nothing_playing_is_idle_but_still_connected()
	{
		var state = MediaStateMapper.Idle("Spotify");

		Assert.That(state.IsConnected, Is.True);
		Assert.That(state.PlaybackState, Is.EqualTo(PlaybackState.Stopped));
		Assert.That(state.DeviceName, Is.EqualTo("Spotify"));
	}

	private static MediaSnapshot Snapshot() => new()
	{
		AppId = "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify",
		AppName = "Spotify",
		Title = "Night Transit",
		Artist = "The Artist",
		AlbumTitle = "Test Album",
		HasThumbnail = true,
		Status = PlaybackState.Playing,
		PlaybackRate = 1,
		StartTime = TimeSpan.Zero,
		EndTime = TimeSpan.FromMinutes(3),
		Position = TimeSpan.FromSeconds(30),
		LastUpdatedTime = _reportedAt,
	};
}
