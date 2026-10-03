using MacroDeck.Sdk.MusicPlayer;
using NUnit.Framework;
using SystemMedia.Core;

namespace SystemMedia.Tests;

[TestFixture]
public sealed class DroppedTimelineRestorerTests
{
	private static readonly DateTimeOffset _started = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

	[Test]
	public void A_timeline_cleared_on_pause_keeps_its_length_and_stops_where_it_paused()
	{
		var restorer = new DroppedTimelineRestorer();
		restorer.Apply(Video(PlaybackState.Playing, TimeSpan.FromSeconds(60), _started));

		var result = restorer.Apply(Cleared(PlaybackState.Paused, _started.AddSeconds(20)));

		Assert.That(result.EndTime, Is.EqualTo(TimeSpan.FromMinutes(10)));
		Assert.That(result.Position, Is.EqualTo(TimeSpan.FromSeconds(80)));
	}

	[Test]
	public void A_timeline_cleared_on_resume_runs_on_from_where_it_paused()
	{
		var restorer = new DroppedTimelineRestorer();
		restorer.Apply(Video(PlaybackState.Playing, TimeSpan.FromSeconds(60), _started));
		restorer.Apply(Cleared(PlaybackState.Paused, _started.AddSeconds(20)));

		var resumed = _started.AddMinutes(5);
		var result = restorer.Apply(Cleared(PlaybackState.Playing, resumed));

		Assert.That(result.EndTime, Is.EqualTo(TimeSpan.FromMinutes(10)));
		Assert.That(result.Position, Is.EqualTo(TimeSpan.FromSeconds(80)));
		Assert.That(result.LastUpdatedTime, Is.EqualTo(resumed));
	}

	[Test]
	public void A_status_that_changes_before_the_timeline_is_cleared_still_counts_as_a_pause()
	{
		var restorer = new DroppedTimelineRestorer();
		restorer.Apply(Video(PlaybackState.Playing, TimeSpan.FromSeconds(60), _started));
		restorer.Apply(Video(PlaybackState.Paused, TimeSpan.FromSeconds(60), _started));

		var result = restorer.Apply(Cleared(PlaybackState.Paused, _started.AddSeconds(20)));

		Assert.That(result.Position, Is.EqualTo(TimeSpan.FromSeconds(80)));
	}

	[Test]
	public void A_timeline_cleared_by_a_seek_stays_gone()
	{
		var restorer = new DroppedTimelineRestorer();
		restorer.Apply(Video(PlaybackState.Playing, TimeSpan.FromSeconds(60), _started));

		var result = restorer.Apply(Cleared(PlaybackState.Playing, _started.AddSeconds(20)));

		Assert.That(result.HasDuration, Is.False);
	}

	[Test]
	public void The_next_track_never_inherits_the_previous_timeline()
	{
		var restorer = new DroppedTimelineRestorer();
		restorer.Apply(Video(PlaybackState.Playing, TimeSpan.FromSeconds(60), _started));

		var result = restorer.Apply(Cleared(PlaybackState.Paused, _started.AddSeconds(20)) with { Title = "Next video" });

		Assert.That(result.HasDuration, Is.False);
	}

	[Test]
	public void A_reported_timeline_always_wins()
	{
		var restorer = new DroppedTimelineRestorer();
		restorer.Apply(Video(PlaybackState.Playing, TimeSpan.FromSeconds(60), _started));
		restorer.Apply(Cleared(PlaybackState.Paused, _started.AddSeconds(20)));

		var result = restorer.Apply(Video(PlaybackState.Paused, TimeSpan.FromSeconds(300), _started.AddSeconds(30)));

		Assert.That(result.Position, Is.EqualTo(TimeSpan.FromSeconds(300)));
	}

	private static MediaSnapshot Video(PlaybackState status, TimeSpan position, DateTimeOffset stamp) => new()
	{
		AppId = "308046B0AF4A39CB",
		AppName = "Firefox",
		Title = "A long video",
		Artist = "A channel",
		Status = status,
		EndTime = TimeSpan.FromMinutes(10),
		Position = position,
		LastUpdatedTime = stamp,
	};

	private static MediaSnapshot Cleared(PlaybackState status, DateTimeOffset stamp) =>
		Video(status, TimeSpan.Zero, stamp) with { EndTime = TimeSpan.Zero };
}
