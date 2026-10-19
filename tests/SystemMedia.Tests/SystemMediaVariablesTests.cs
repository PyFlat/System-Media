using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Variables;
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

	[TestCase("Spotify.exe")]
	[TestCase("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic.Very.Long.Application.Identifier.That.Exceeds.Limits")]
	[TestCase("---")]
	public void Every_app_variable_has_a_valid_id_of_its_own(string appId)
	{
		var ids = SystemMediaVariables.ForApp(appId, "App").Select(pair => pair.Definition.ResolvedId).ToList();

		Assert.That(ids, Has.All.Matches(InstanceIdsTests.LocalIdPattern).And.All.Length.LessThanOrEqualTo(64));
		Assert.That(ids, Is.Unique);
		Assert.That(ids, Has.None.AnyOf(SystemMediaVariables.All.Select(variable => variable.ResolvedId).ToArray()));
	}

	[Test]
	public void An_app_variable_is_named_after_the_app_and_reads_like_its_any_app_variable()
	{
		var (definition, currentId) = SystemMediaVariables.ForApp("Spotify.exe", "Spotify").Single(pair => pair.Definition.Name!.EndsWith("_artist", StringComparison.Ordinal));

		Assert.That(definition.Name, Is.EqualTo("system_media_spotify_exe_b527fa0a_artist"));
		Assert.That(currentId, Is.EqualTo("system-media-artist"));
	}

	[Test]
	public void App_variables_are_declared_as_templates_without_an_id()
	{
		Assert.That(SystemMediaVariables.AppTemplates.Select(template => template.Name), Has.All.StartsWith("system_media_<app>_"));
		Assert.That(SystemMediaVariables.AppTemplates.Select(template => template.ResolvedId), Has.All.Null);
		Assert.That(SystemMediaVariables.AppTemplates, Has.Count.EqualTo(SystemMediaVariables.PerApp));
	}

	[Test]
	public void Apps_past_the_eager_variable_limit_get_no_variables()
	{
		var apps = Enumerable.Range(0, AppVariables.MaxApps + 3).Select(index => ($"app{index}", $"App {index}"));

		var variables = new AppVariables(apps);

		Assert.That(variables.Definitions, Has.Count.EqualTo(AppVariables.MaxApps * SystemMediaVariables.PerApp));
		Assert.That(SystemMediaVariables.All.Count + variables.Definitions.Count, Is.LessThanOrEqualTo(VariableLimits.MaxEagerVariablesPerProvider));
	}

	[Test]
	public void An_app_variable_id_leads_back_to_its_app()
	{
		var variables = new AppVariables([("Spotify.exe", "Spotify")]);

		Assert.That(variables.Find("system-media-spotify-exe-b527fa0a-volume"), Is.EqualTo(new AppVariables.Target("Spotify.exe", SystemMediaVariables.VolumeId)));
		Assert.That(variables.Find(SystemMediaVariables.VolumeId), Is.Null);
	}

	[Test]
	public void A_stream_without_a_timeline_has_no_progress()
	{
		var stream = _playing with { Position = null, Duration = null };

		Assert.That(SystemMediaVariables.Read("system-media-progress-percentage", stream).Value, Is.Null);
	}
}
