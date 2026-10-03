using System.Buffers.Binary;
using System.Text;
using MacroDeck.Sdk.MusicPlayer;
using NUnit.Framework;
using SystemMedia.Players;

namespace SystemMedia.Tests.Players;

[TestFixture]
public sealed class MpvPlayerTests
{
	[Test]
	public async Task It_connects_only_to_a_server_it_saw_on_the_poll_before()
	{
		await using var mpv = new FakeMpv(UniqueName());
		await using var source = Create(mpv.Endpoint);

		var first = await source.ReadAsync(CancellationToken.None);
		var second = await source.ReadAsync(CancellationToken.None);

		Assert.That(first, Is.Null);
		Assert.That(second, Is.Not.Null);
	}

	[Test]
	public async Task A_reading_takes_tags_in_any_case_and_the_timeline_and_volume()
	{
		await using var mpv = new FakeMpv(UniqueName());
		await using var source = Create(mpv.Endpoint);

		var reading = await ReadConnectedAsync(source);

		Assert.That(reading, Is.EqualTo(new PlayerReading(
			PlaybackState.Playing, "Numb", "Linkin Park", "Meteora",
			TimeSpan.FromSeconds(12.5), TimeSpan.FromSeconds(185), 80, HasCover: true)));
	}

	[Test]
	public async Task A_file_without_tags_is_named_by_its_media_title()
	{
		await using var mpv = new FakeMpv(UniqueName());
		mpv.Properties["metadata"] = null;
		await using var source = Create(mpv.Endpoint);

		Assert.That((await ReadConnectedAsync(source))!.Title, Is.EqualTo("song.mp3"));
	}

	[Test]
	public async Task An_idle_mpv_is_not_playing_anything()
	{
		await using var mpv = new FakeMpv(UniqueName());
		mpv.Properties["idle-active"] = true;
		await using var source = Create(mpv.Endpoint);

		Assert.That(await ReadConnectedAsync(source), Is.Null);
	}

	[Test]
	public async Task A_video_offers_a_thumbnail_only_once_it_is_past_its_first_seconds()
	{
		await using var mpv = new FakeMpv(UniqueName());
		mpv.Properties["current-tracks/video"] = new System.Text.Json.Nodes.JsonObject { ["albumart"] = false };
		mpv.Properties["time-pos"] = 1.0;
		await using var source = Create(mpv.Endpoint);

		var early = await ReadConnectedAsync(source);
		mpv.Properties["time-pos"] = 30.0;
		var later = await source.ReadAsync(CancellationToken.None);

		Assert.That(early!.HasCover, Is.False);
		Assert.That(later!.HasCover, Is.True);
	}

	[Test]
	public async Task The_cover_is_a_screenshot_of_the_video_track_and_leaves_no_file_behind()
	{
		await using var mpv = new FakeMpv(UniqueName());
		await using var source = Create(mpv.Endpoint);
		await ReadConnectedAsync(source);

		var cover = await source.ReadCoverAsync(CancellationToken.None);

		Assert.That(cover!.Data, Is.EqualTo(FakeMpv.Cover));
		Assert.That(cover.MimeType, Is.EqualTo("image/png"));
		Assert.That(mpv.Commands, Has.One.StartsWith("screenshot-to-file video"));
		Assert.That(Directory.EnumerateFiles(Path.GetTempPath(), "system-media-cover-*"), Is.Empty);
	}

	[Test]
	public async Task Commands_become_mpv_commands_and_a_refused_one_reports_false()
	{
		await using var mpv = new FakeMpv(UniqueName());
		await using var source = Create(mpv.Endpoint);
		await ReadConnectedAsync(source);

		Assert.That(await source.SendAsync(PlayerCommand.Pause, null, CancellationToken.None), Is.True);
		Assert.That(await source.SendAsync(PlayerCommand.Seek, 42.25, CancellationToken.None), Is.True);
		Assert.That(await source.SendAsync(PlayerCommand.Next, null, CancellationToken.None), Is.False);

		Assert.That(mpv.Commands, Is.EqualTo((List<string>)["set_property pause true", "seek 42.25 absolute", "playlist-next"]));
	}

	[Test]
	public async Task A_pattern_finds_a_server_named_after_a_process_id()
	{
		var name = UniqueName();
		await using var mpv = new FakeMpv(name + "-1a2b");
		await using var source = new MpvPlayer([name + "-*"]);

		Assert.That(await ReadConnectedAsync(source), Is.Not.Null);
	}

	[Test]
	public async Task A_qt_single_application_message_is_length_prefixed_and_needs_an_ack()
	{
		var received = new TaskCompletionSource<string>();
		await using var server = new FakeLocalServer(UniqueName(), async (stream, cancellationToken) =>
		{
			var header = new byte[4];
			await stream.ReadExactlyAsync(header, cancellationToken);
			var body = new byte[BinaryPrimitives.ReadUInt32BigEndian(header)];
			await stream.ReadExactlyAsync(body, cancellationToken);
			received.SetResult(Encoding.UTF8.GetString(body));
			await stream.WriteAsync("ack"u8.ToArray(), cancellationToken);
		});

		var sent = await QtSingleApplication.SendAsync(server.Endpoint, "action play_next", CancellationToken.None);

		Assert.That(sent, Is.True);
		Assert.That(await received.Task, Is.EqualTo("action play_next"));
	}

	[Test]
	public async Task A_qt_message_to_an_application_that_is_not_running_fails()
	{
		Assert.That(await QtSingleApplication.SendAsync(UniqueName(), "action play_next", CancellationToken.None), Is.False);
	}

	[Test]
	public void SmPlayer_leaves_the_volume_alone_because_it_restarts_mpv_per_file()
	{
		var smplayer = new SmPlayer();

		Assert.That(smplayer.AppId, Is.EqualTo("player:smplayer"));
		Assert.That(smplayer.Supports(PlayerCommand.Volume), Is.False);
		Assert.That(smplayer.Supports(PlayerCommand.Next), Is.True);
	}

	private static MpvPlayer Create(string endpoint) => new([endpoint]);

	private static async Task<PlayerReading?> ReadConnectedAsync(MpvPlayer source)
	{
		await source.ReadAsync(CancellationToken.None);
		return await source.ReadAsync(CancellationToken.None);
	}

	// A Unix socket path is limited to about a hundred characters.
	private static string UniqueName() => "sm-test-" + Guid.NewGuid().ToString("N")[..8];
}
