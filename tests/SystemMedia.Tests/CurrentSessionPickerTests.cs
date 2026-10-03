using NUnit.Framework;
using SystemMedia.Core;

namespace SystemMedia.Tests;

[TestFixture]
public sealed class CurrentSessionPickerTests
{
	private const string Firefox = "308046B0AF4A39CB";
	private const string Vlc = "vlc.exe";

	[Test]
	public void Windows_current_session_wins_while_it_plays()
	{
		Assert.That(CurrentSessionPicker.Pick([new(Firefox, true), new(Vlc, true)], Vlc, shownLast: Firefox), Is.EqualTo(Vlc));
	}

	[Test]
	public void A_playing_app_wins_over_a_paused_one_windows_still_calls_current()
	{
		// Pausing the browser and starting VLC: Windows can keep pointing at the browser for a moment.
		Assert.That(CurrentSessionPicker.Pick([new(Firefox, false), new(Vlc, true)], Firefox, shownLast: Firefox), Is.EqualTo(Vlc));
	}

	[Test]
	public void With_two_apps_playing_and_windows_undecided_the_one_shown_stays()
	{
		Assert.That(CurrentSessionPicker.Pick([new(Firefox, true), new(Vlc, true)], windowsCurrent: null, shownLast: Vlc), Is.EqualTo(Vlc));
	}

	[Test]
	public void With_nothing_playing_windows_current_session_is_shown()
	{
		Assert.That(CurrentSessionPicker.Pick([new(Firefox, false), new(Vlc, false)], Firefox, shownLast: Vlc), Is.EqualTo(Firefox));
	}

	[Test]
	public void With_nothing_playing_and_no_windows_choice_the_one_shown_stays()
	{
		Assert.That(CurrentSessionPicker.Pick([new(Firefox, false), new(Vlc, false)], windowsCurrent: null, shownLast: Vlc), Is.EqualTo(Vlc));
	}

	[Test]
	public void An_app_that_closed_is_not_picked_because_it_was_shown_last()
	{
		Assert.That(CurrentSessionPicker.Pick([new(Firefox, false)], windowsCurrent: null, shownLast: Vlc), Is.EqualTo(Firefox));
		Assert.That(CurrentSessionPicker.Pick([], windowsCurrent: null, shownLast: Vlc), Is.Null);
	}

	[Test]
	public void An_app_cycled_to_wins_over_windows_current_session_while_it_plays()
	{
		Assert.That(CurrentSessionPicker.Pick([new(Firefox, true), new(Vlc, true)], Vlc, shownLast: Vlc, chosen: Firefox), Is.EqualTo(Firefox));
		Assert.That(CurrentSessionPicker.Pick([new(Firefox, false), new(Vlc, true)], Vlc, shownLast: Firefox, chosen: Firefox), Is.EqualTo(Vlc));
	}

	[Test]
	public void The_next_playing_app_wraps_around_and_skips_paused_ones()
	{
		const string Spotify = "Spotify.exe";
		SessionCandidate[] sessions = [new(Firefox, true), new(Spotify, false), new(Vlc, true)];

		Assert.That(CurrentSessionPicker.NextPlaying(sessions, Firefox), Is.EqualTo(Vlc));
		Assert.That(CurrentSessionPicker.NextPlaying(sessions, Vlc), Is.EqualTo(Firefox));
		Assert.That(CurrentSessionPicker.NextPlaying(sessions, Spotify), Is.EqualTo(Firefox));
	}

	[Test]
	public void There_is_no_next_playing_app_when_only_the_shown_one_plays()
	{
		Assert.That(CurrentSessionPicker.NextPlaying([new(Firefox, false), new(Vlc, true)], Vlc), Is.Null);
		Assert.That(CurrentSessionPicker.NextPlaying([new(Firefox, false)], Firefox), Is.Null);
	}
}
