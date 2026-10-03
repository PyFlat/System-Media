using MacroDeck.Sdk.MusicPlayer;
using NUnit.Framework;
using SystemMedia.Core;

namespace SystemMedia.Tests;

[TestFixture]
public sealed class CurrentAppSelectionTests
{
	private const string Firefox = "308046B0AF4A39CB";
	private const string Vlc = "vlc.exe";

	private static readonly TimeSpan _cycle = TimeSpan.FromSeconds(10);

	[Test]
	public void Without_cycling_windows_current_session_keeps_winning()
	{
		var (platform, time, selection) = TwoPlaying();

		Assert.That(selection.Current(), Is.EqualTo(Vlc));
		time.Advance(TimeSpan.FromMinutes(5));
		Assert.That(selection.Current(), Is.EqualTo(Vlc));
		Assert.That(platform.SystemCurrent, Is.EqualTo(Vlc));
	}

	[Test]
	public void Cycling_takes_turns_between_playing_apps_after_the_interval()
	{
		var (_, time, selection) = TwoPlaying();

		Assert.That(selection.Current(_cycle), Is.EqualTo(Vlc));
		time.Advance(TimeSpan.FromSeconds(9));
		Assert.That(selection.Current(_cycle), Is.EqualTo(Vlc));
		time.Advance(TimeSpan.FromSeconds(1));
		Assert.That(selection.Current(_cycle), Is.EqualTo(Firefox));
		time.Advance(TimeSpan.FromSeconds(10));
		Assert.That(selection.Current(_cycle), Is.EqualTo(Vlc));
	}

	[Test]
	public void Cycling_does_not_leave_the_only_playing_app()
	{
		var (platform, time, selection) = TwoPlaying();
		platform.Sessions[Firefox] = platform.Sessions[Firefox] with { Status = PlaybackState.Paused };

		Assert.That(selection.Current(_cycle), Is.EqualTo(Vlc));
		time.Advance(TimeSpan.FromSeconds(30));
		Assert.That(selection.Current(_cycle), Is.EqualTo(Vlc));
	}

	[Test]
	public void A_reader_without_cycling_follows_the_app_a_cycling_widget_moved_to()
	{
		var (_, time, selection) = TwoPlaying();

		Assert.That(selection.Current(_cycle), Is.EqualTo(Vlc));
		time.Advance(_cycle);
		Assert.That(selection.Current(_cycle), Is.EqualTo(Firefox));

		Assert.That(selection.Current(), Is.EqualTo(Firefox), "so the widget's buttons control the app it shows");
	}

	[Test]
	public void Showing_the_next_playing_app_sticks_while_it_plays()
	{
		var (platform, _, selection) = TwoPlaying();

		Assert.That(selection.Current(), Is.EqualTo(Vlc));
		Assert.That(selection.ShowNextPlaying(), Is.True);
		Assert.That(selection.Current(), Is.EqualTo(Firefox));

		platform.Sessions[Firefox] = platform.Sessions[Firefox] with { Status = PlaybackState.Paused };
		Assert.That(selection.Current(), Is.EqualTo(Vlc));
		Assert.That(selection.ShowNextPlaying(), Is.False);
	}

	private static (FakeMediaPlatform Platform, ManualTime Time, CurrentAppSelection Selection) TwoPlaying()
	{
		var platform = new FakeMediaPlatform { SystemCurrent = Vlc };
		platform.Sessions[Firefox] = FakeMediaPlatform.Playing(Firefox, "Video");
		platform.Sessions[Vlc] = FakeMediaPlatform.Playing(Vlc, "Movie");
		var time = new ManualTime();
		return (platform, time, new CurrentAppSelection(platform, time));
	}

	private sealed class ManualTime : TimeProvider
	{
		private long _ticks;

		public override long TimestampFrequency => TimeSpan.TicksPerSecond;

		public override long GetTimestamp() => _ticks;

		internal void Advance(TimeSpan by) => _ticks += by.Ticks;
	}
}
