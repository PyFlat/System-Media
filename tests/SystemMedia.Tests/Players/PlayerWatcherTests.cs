using MacroDeck.Sdk.MusicPlayer;
using NUnit.Framework;
using Serilog;
using SystemMedia.Players;

namespace SystemMedia.Tests.Players;

[TestFixture]
public sealed class PlayerWatcherTests
{
	private static readonly TimeSpan _fastPolls = TimeSpan.FromMilliseconds(250);

	[Test]
	public async Task Toggle_falls_back_to_pause_while_playing_when_the_player_has_no_toggle()
	{
		var source = new FakeSource(PlayerCommand.Play, PlayerCommand.Pause) { Reading = Playing("Song") };
		await using var player = await StartedAsync(source);

		Assert.That(await player.SendAsync(PlayerCommand.Toggle, null, CancellationToken.None), Is.True);
		Assert.That(source.Sent, Is.EqualTo(new[] { PlayerCommand.Pause }));
	}

	[Test]
	public async Task Play_falls_back_to_toggle_and_is_a_no_op_while_already_playing()
	{
		var source = new FakeSource(PlayerCommand.Toggle) { Reading = Playing("Song") };
		await using var player = await StartedAsync(source);

		Assert.That(await player.SendAsync(PlayerCommand.Play, null, CancellationToken.None), Is.True);
		Assert.That(await player.SendAsync(PlayerCommand.Pause, null, CancellationToken.None), Is.True);
		Assert.That(source.Sent, Is.EqualTo(new[] { PlayerCommand.Toggle }));
	}

	[Test]
	public async Task A_volume_the_player_cannot_set_is_not_shown()
	{
		var source = new FakeSource { Reading = Playing("Song") with { VolumePercent = 50 } };
		await using var player = await StartedAsync(source);

		Assert.That(player.VolumePercent(), Is.Null);
	}

	[Test]
	public async Task A_command_the_player_has_no_way_to_run_fails()
	{
		await using var player = await StartedAsync(new FakeSource { Reading = Playing("Song") });

		Assert.That(await player.SendAsync(PlayerCommand.Next, null, CancellationToken.None), Is.False);
	}

	[Test]
	public async Task A_player_that_stops_answering_stays_for_a_moment_so_a_restart_between_tracks_is_not_a_change()
	{
		var time = new ManualTime();
		var source = new FakeSource { Reading = Playing("Song") };
		await using var player = await StartedAsync(source, time: time);

		source.Reading = null;
		await Task.Delay(_fastPolls * 3);
		Assert.That(player.IsPresent, Is.True);

		time.Advance(TimeSpan.FromSeconds(6));
		await WaitUntilAsync(() => !player.IsPresent);
	}

	[Test]
	public async Task The_cover_is_read_once_per_track()
	{
		var source = new FakeSource { Reading = Playing("Song") with { HasCover = true } };
		await using var player = await StartedAsync(source);

		await player.ReadCoverAsync(CancellationToken.None);
		await player.ReadCoverAsync(CancellationToken.None);
		source.Reading = Playing("Next song") with { HasCover = true };
		await WaitUntilAsync(() => player.Snapshot()?.Title == "Next song");
		await player.ReadCoverAsync(CancellationToken.None);

		Assert.That(source.CoverReads, Is.EqualTo(2));
	}

	[Test]
	public async Task The_snapshot_carries_the_timeline_for_the_shared_mapping()
	{
		var source = new FakeSource { Reading = Playing("Song") with { Position = TimeSpan.FromSeconds(30), Duration = TimeSpan.FromMinutes(3) } };
		await using var player = await StartedAsync(source);

		var snapshot = player.Snapshot()!;

		Assert.That(snapshot.AppId, Is.EqualTo("player:test"));
		Assert.That(snapshot.Position, Is.EqualTo(TimeSpan.FromSeconds(30)));
		Assert.That(snapshot.EndTime, Is.EqualTo(TimeSpan.FromMinutes(3)));
		Assert.That(snapshot.Status, Is.EqualTo(PlaybackState.Playing));
	}

	private static PlayerReading Playing(string title) => new(PlaybackState.Playing, title);

	private static async Task<PlayerWatcher> StartedAsync(FakeSource source, TimeProvider? time = null)
	{
		var player = new PlayerWatcher(source, time ?? TimeProvider.System, new LoggerConfiguration().CreateLogger());
		player.Start();
		await WaitUntilAsync(() => player.IsPresent);
		return player;
	}

	private static async Task WaitUntilAsync(Func<bool> condition)
	{
		for (var i = 0; i < 100 && !condition(); i++)
		{
			await Task.Delay(50);
		}

		Assert.That(condition(), Is.True, "timed out");
	}

	private sealed class FakeSource(params PlayerCommand[] supported) : Player
	{
		internal volatile PlayerReading? Reading;

		internal override string Id => "test";

		internal override string Name => "Test";

		internal override TimeSpan Interval => _fastPolls;

		internal List<PlayerCommand> Sent { get; } = [];

		internal int CoverReads { get; private set; }

		internal override Task<PlayerReading?> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Reading);

		internal override Task<MusicPlayerArtwork?> ReadCoverAsync(CancellationToken cancellationToken)
		{
			CoverReads++;
			return Task.FromResult<MusicPlayerArtwork?>(new MusicPlayerArtwork([1], "image/png"));
		}

		internal override bool Supports(PlayerCommand command) => supported.Contains(command);

		internal override Task<bool> SendAsync(PlayerCommand command, double? value, CancellationToken cancellationToken)
		{
			Sent.Add(command);
			return Task.FromResult(true);
		}
	}

	private sealed class ManualTime : TimeProvider
	{
		private DateTimeOffset _now = DateTimeOffset.UtcNow;

		public override DateTimeOffset GetUtcNow() => _now;

		internal void Advance(TimeSpan by) => _now += by;
	}
}
