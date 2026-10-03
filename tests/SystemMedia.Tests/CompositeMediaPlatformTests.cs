using MacroDeck.Sdk.MusicPlayer;
using NUnit.Framework;
using Serilog;
using SystemMedia.Core;
using SystemMedia.Platform;
using SystemMedia.Players;

namespace SystemMedia.Tests;

[TestFixture]
public sealed class PlayersSourceTests
{
	[Test]
	public void Only_player_ids_belong_to_the_players_source()
	{
		using var source = new PlayersSource(new LoggerConfiguration().CreateLogger(), TimeProvider.System);

		Assert.That(source.Owns("player:smplayer"), Is.True);
		Assert.That(source.Owns("Spotify.exe"), Is.False);
	}

	[Test]
	public void A_player_is_named_before_it_ever_plays()
	{
		using var source = new PlayersSource(new LoggerConfiguration().CreateLogger(), TimeProvider.System);

		Assert.That(source.ResolveApp("player:smplayer").DisplayName, Is.EqualTo("SMPlayer"));
		Assert.That(source.ResolveApp("player:mpv").DisplayName, Is.EqualTo("mpv"));
	}
}

[TestFixture]
public sealed class CompositeMediaPlatformTests
{
	[Test]
	public async Task Each_app_id_goes_to_the_source_that_owns_it()
	{
		var system = new FakeMediaPlatform();
		system.Sessions["Spotify.exe"] = FakeMediaPlatform.Playing("Spotify.exe", "From Windows");
		var custom = new FakeMediaSource();
		custom.Sessions["player:mine"] = FakeMediaPlatform.Playing("player:mine", "From a player");
		using var platform = new CompositeMediaPlatform(system, [custom]);

		Assert.That(platform.GetSessions().Select(session => session.AppId), Is.EquivalentTo((List<string>)["Spotify.exe", "player:mine"]));
		Assert.That((await platform.ReadAsync("player:mine", CancellationToken.None))!.Title, Is.EqualTo("From a player"));
		Assert.That((await platform.ReadAsync("Spotify.exe", CancellationToken.None))!.Title, Is.EqualTo("From Windows"));
	}

	[Test]
	public void A_player_without_its_own_icon_or_volume_uses_the_systems_for_its_executable()
	{
		var system = new FakeMediaPlatform { SupportsVolume = true };
		var custom = new FakeMediaSource();
		using var platform = new CompositeMediaPlatform(system, [custom]);
		var app = platform.ResolveApp("player:mine");

		Assert.That(platform.HasAppIcon(app), Is.True);
		Assert.That(platform.GetVolumePercent(app), Is.EqualTo(42));
	}

	[Test]
	public void It_counts_as_started_when_only_a_source_is()
	{
		using var platform = new CompositeMediaPlatform(new FakeMediaPlatform { IsStarted = false }, [new FakeMediaSource()]);

		Assert.That(platform.IsStarted, Is.True);
	}

	private sealed class FakeMediaSource : IMediaSource
	{
		private readonly FakeMediaPlatform _inner = new();

		internal Dictionary<string, SystemMedia.Core.MediaSnapshot> Sessions => _inner.Sessions;

		public event EventHandler? SessionsChanged
		{
			add { }
			remove { }
		}

		public bool IsStarted => true;

		public bool Owns(string appId) => appId.StartsWith("player:", StringComparison.Ordinal);

		public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

		public IReadOnlyList<SystemMedia.Core.SessionCandidate> GetSessions() => _inner.GetSessions();

		public string? GetSystemCurrentAppId() => null;

		public Task<SystemMedia.Core.MediaSnapshot?> ReadAsync(string appId, CancellationToken cancellationToken) => _inner.ReadAsync(appId, cancellationToken);

		public Task<MusicPlayerArtwork?> ReadThumbnailAsync(string appId, CancellationToken cancellationToken) => _inner.ReadThumbnailAsync(appId, cancellationToken);

		public Task<bool> SendAsync(string appId, MediaCommand command, CancellationToken cancellationToken) => _inner.SendAsync(appId, command, cancellationToken);

		public Task<bool> SeekAsync(string appId, TimeSpan position, CancellationToken cancellationToken) => _inner.SeekAsync(appId, position, cancellationToken);

		public Task<bool> SetShuffleAsync(string appId, bool enabled, CancellationToken cancellationToken) => Task.FromResult(false);

		public Task<bool> SetRepeatModeAsync(string appId, RepeatMode mode, CancellationToken cancellationToken) => Task.FromResult(false);

		public SystemMedia.Core.AppIdentity ResolveApp(string appId) => new(appId, "Mine", "/apps/mine", PackageFamilyName: null);

		public bool HasAppIcon(SystemMedia.Core.AppIdentity app) => false;

		public Task<MusicPlayerArtwork?> GetAppIconAsync(SystemMedia.Core.AppIdentity app, CancellationToken cancellationToken) => Task.FromResult<MusicPlayerArtwork?>(null);

		public int? GetVolumePercent(SystemMedia.Core.AppIdentity app) => null;

		public Task<bool> SetVolumePercentAsync(SystemMedia.Core.AppIdentity app, int volumePercent, CancellationToken cancellationToken) => Task.FromResult(false);

		public IReadOnlyList<MacroDeck.Sdk.Issues.IntegrationIssue> GetIssues() => [];

		public Task<MacroDeck.Sdk.Issues.IssueResolution?> ResolveIssueAsync(string issueId, CancellationToken cancellationToken) =>
			Task.FromResult<MacroDeck.Sdk.Issues.IssueResolution?>(null);

		public void Dispose()
		{
		}
	}
}
