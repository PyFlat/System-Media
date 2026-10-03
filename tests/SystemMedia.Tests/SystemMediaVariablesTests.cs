using MacroDeck.Sdk.MusicPlayer;
using NUnit.Framework;
using SystemMedia.Platform;
using SystemMedia.Variables;

namespace SystemMedia.Tests;

[TestFixture]
public sealed class SystemMediaVariablesTests
{
	private static readonly MusicPlayerState _playing = new()
	{
		IsConnected = true,
		PlaybackState = PlaybackState.Playing,
		TrackName = "Unbreakable",
		Artists = ["First", "Second"],
		AlbumName = "Album",
		DeviceName = "VLC media player",
		Position = TimeSpan.FromSeconds(64.4),
		Duration = TimeSpan.FromSeconds(256),
		VolumePercent = 80,
		RepeatMode = RepeatMode.Context,
	};

	[Test]
	public void Every_variable_has_a_valid_id_and_a_display_name()
	{
		Assert.That(SystemMediaVariables.All.Select(variable => variable.ResolvedId), Has.All.Matches(InstanceIdsTests.LocalIdPattern));
		Assert.That(SystemMediaVariables.All.Select(variable => variable.ResolvedId), Is.Unique);
	}

	[TestCase("system-media-app-name", "VLC media player")]
	[TestCase("system-media-track-name", "Unbreakable")]
	[TestCase("system-media-artist", "First, Second")]
	[TestCase("system-media-playback-state", "playing")]
	[TestCase("system-media-is-playing", true)]
	[TestCase("system-media-position", 64d)]
	[TestCase("system-media-duration", 256d)]
	[TestCase("system-media-progress-percentage", 25)]
	[TestCase("system-media-volume", 80)]
	[TestCase("system-media-repeat-mode", "context")]
	public void A_variable_reads_its_value_from_the_current_state(string id, object expected)
	{
		Assert.That(SystemMediaVariables.Read(id, _playing).Value, Is.EqualTo(expected));
	}

	[TestCase("system-media-output-device", nameof(AudioDeviceRole.Output))]
	[TestCase("system-media-input-device", nameof(AudioDeviceRole.Input))]
	[TestCase("system-media-communication-output-device", nameof(AudioDeviceRole.CommunicationOutput))]
	[TestCase("system-media-communication-input-device", nameof(AudioDeviceRole.CommunicationInput))]
	public void A_device_variable_is_a_writable_default_device(string id, string role)
	{
		var variable = SystemMediaVariables.All.Single(candidate => candidate.ResolvedId == id);

		Assert.That(variable.CanWrite, Is.True);
		Assert.That(SystemMediaVariables.DeviceRole(id).ToString(), Is.EqualTo(role));
	}

	[Test]
	public void A_state_variable_is_no_device()
	{
		Assert.That(SystemMediaVariables.DeviceRole(SystemMediaVariables.VolumeId), Is.Null);
	}

	[Test]
	public void The_position_slider_ends_at_the_track_duration()
	{
		var reading = SystemMediaVariables.Read(SystemMediaVariables.PositionId, _playing);

		Assert.That(reading.Max, Is.EqualTo(256));
	}

	[Test]
	public void A_stream_without_a_timeline_has_no_progress()
	{
		var stream = _playing with { Position = null, Duration = null };

		Assert.That(SystemMediaVariables.Read("system-media-progress-percentage", stream).Value, Is.Null);
	}
}
