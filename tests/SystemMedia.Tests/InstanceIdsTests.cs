using NUnit.Framework;
using SystemMedia.Core;

namespace SystemMedia.Tests;

[TestFixture]
public sealed class InstanceIdsTests
{
	internal const string LocalIdPattern = @"\A[a-z][a-z0-9]*(?:-[a-z0-9]+)*\z";

	private static readonly string[] _appUserModelIds =
	[
		"SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify",
		"308046B0AF4A39CB",
		"308046B0AF4A39CB;PrivateBrowsingAUMID",
		@"{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}\VideoLAN\VLC\vlc.exe",
		"Spotify.exe",
		"Chrome",
		"---",
		"Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic.Very.Long.Application.Identifier.That.Exceeds.Limits",
	];

	[TestCaseSource(nameof(_appUserModelIds))]
	public void An_app_id_becomes_a_valid_local_id(string appUserModelId)
	{
		var id = InstanceIds.ForApp(appUserModelId);

		Assert.That(id, Does.Match(LocalIdPattern));
		Assert.That(id, Has.Length.LessThanOrEqualTo(64));
	}

	// Pinned: widgets persist it.
	[Test]
	public void The_mapping_never_changes_so_a_bound_widget_survives_restarts_and_updates()
	{
		Assert.That(InstanceIds.ForApp("Spotify.exe"), Is.EqualTo("app-spotify-exe-b527fa0a"));
	}

	[Test]
	public void Ids_that_slug_the_same_still_map_to_different_instances()
	{
		Assert.That(InstanceIds.ForApp("Spotify.exe"), Is.Not.EqualTo(InstanceIds.ForApp("spotify-exe")));
	}

	[Test]
	public void No_app_can_take_the_current_instance_id()
	{
		Assert.That(_appUserModelIds.Select(InstanceIds.ForApp), Has.None.EqualTo(InstanceIds.Current));
	}
}
