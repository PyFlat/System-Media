using MacroDeck.Localization;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Variables;
using NUnit.Framework;
using SystemMedia.Core;
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

	[TestCase("Spotify.exe")]
	[TestCase("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic.Very.Long.Application.Identifier.That.Exceeds.Limits")]
	[TestCase("---")]
	public void Every_app_variable_is_an_on_demand_variable_in_its_app_folder(string appId)
	{
		var instanceId = InstanceIds.ForApp(appId);
		var variables = SystemMediaVariables.ForApp(instanceId, "App", "app");

		Assert.That(variables.Select(variable => variable.Id), Has.All.Matches(InstanceIdsTests.LocalIdPattern).And.All.Length.LessThanOrEqualTo(256));
		Assert.That(variables.Select(variable => variable.Id), Is.Unique);
		Assert.That(variables.Select(variable => variable.Id), Has.None.AnyOf(SystemMediaVariables.All.Select(variable => variable.ResolvedId).ToArray()));
		Assert.That(variables.Select(variable => variable.Materialization), Has.All.EqualTo(VariableMaterialization.OnDemand));
		Assert.That(variables.Select(variable => variable.ParentId), Has.All.EqualTo(SystemMediaVariables.AppFolderId(instanceId)));
	}

	// Pinned: the host keeps the id of every linked variable.
	[Test]
	public void An_app_variable_is_identified_by_the_app_instance_and_named_after_the_app()
	{
		var keys = SystemMediaVariables.NameKeys([("app-vlc-exe-2900ecd1", "VLC media player")]);
		var artist = SystemMediaVariables.ForApp("app-vlc-exe-2900ecd1", "VLC media player", keys["app-vlc-exe-2900ecd1"])
			.Single(variable => variable.Id!.EndsWith("-artist", StringComparison.Ordinal));

		Assert.That(artist.Id, Is.EqualTo("system-media-app-vlc-exe-2900ecd1-artist"));
		Assert.That(artist.Name, Is.EqualTo("system_media_vlc_media_player_artist"));
	}

	[Test]
	public void Apps_with_the_same_name_are_told_apart_by_their_hash()
	{
		var keys = SystemMediaVariables.NameKeys([("app-firefox-11111111", "Firefox"), ("app-firefox-profile-22222222", "Firefox"), ("app-vlc-exe-33333333", "VLC")]);

		Assert.That(keys["app-firefox-11111111"], Is.EqualTo("firefox_11111111"));
		Assert.That(keys["app-firefox-profile-22222222"], Is.EqualTo("firefox_22222222"));
		Assert.That(keys["app-vlc-exe-33333333"], Is.EqualTo("vlc"));
	}

	[Test]
	public void An_app_name_without_letters_falls_back_to_the_hash()
	{
		Assert.That(SystemMediaVariables.NameKeys([("app-44444444", "音楽")])["app-44444444"], Is.EqualTo("44444444"));
	}

	[TestCase("system-media-app-spotify-exe-b527fa0a-track-name", "system-media-track-name")]
	[TestCase("system-media-app-spotify-exe-b527fa0a-volume", SystemMediaVariables.VolumeId)]
	[TestCase("system-media-app-spotify-exe-b527fa0a", null)]
	[TestCase("system-media-app-spotify-exe-b527fa0a-app-name", null)]
	[TestCase("system-media-app-vlc-exe-0011aabb-artist", null)]
	[TestCase(SystemMediaVariables.VolumeId, null)]
	public void An_app_variable_reads_like_its_any_app_variable(string localId, string? currentId)
	{
		Assert.That(SystemMediaVariables.CurrentIdOf(localId, "app-spotify-exe-b527fa0a"), Is.EqualTo(currentId));
	}

	// The host resolves the text itself and drops a localized argument nested inside it.
	[Test]
	public void An_app_variable_name_carries_the_app_as_plain_text_only()
	{
		var names = SystemMediaVariables.ForApp("app-spotify-exe-b527fa0a", "Spotify", "spotify").Select(variable => variable.DisplayName.Localized!.Value);

		Assert.That(names.Select(name => name.Arguments.Values.Single()), Has.All.EqualTo((LocalizedText)"Spotify"));
	}

	[TestCase("vlc artist", true)]
	[TestCase("VLC Media", true)]
	[TestCase("system_media_vlc_media_player_artist", true)]
	[TestCase("player", true)]
	[TestCase("firefox artist", false)]
	[TestCase("vlc album", false)]
	public void A_search_finds_an_app_variable_by_its_name_or_its_app(string search, bool found)
	{
		var artist = SystemMediaVariables.ForApp("app-vlc-exe-2900ecd1", "VLC media player", "vlc_media_player")
			.Single(variable => variable.Id!.EndsWith("-artist", StringComparison.Ordinal));

		Assert.That(SystemMediaVariables.Matches(artist, "VLC media player", search), Is.EqualTo(found));
	}

	[Test]
	public void An_app_folder_holds_variables_but_is_none()
	{
		var folder = SystemMediaVariables.AppFolder("app-spotify-exe-b527fa0a", "Spotify");

		Assert.That(folder.Id, Is.EqualTo("system-media-app-spotify-exe-b527fa0a"));
		Assert.That(folder.IsContainer, Is.True);
		Assert.That(folder.IsBindable, Is.False);
	}

	[Test]
	public void A_stream_without_a_timeline_has_no_progress()
	{
		var stream = _playing with { Position = null, Duration = null };

		Assert.That(SystemMediaVariables.Read("system-media-progress-percentage", stream).Value, Is.Null);
	}
}
