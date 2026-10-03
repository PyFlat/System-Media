using SystemMedia.Platform;

namespace SystemMedia.Core;

internal sealed class CurrentAppSelection(IMediaPlatform platform, TimeProvider time)
{
	private readonly Lock _gate = new();

	private string? _shownLast;
	private string? _chosen;
	private long _shownSince;

	// Shared by every "Any app" player, so a cycling widget also moves the plain player and the actions.
	// With a zero interval the system picks the app; otherwise the playing apps take turns.
	internal string? Current(TimeSpan cycleInterval = default)
	{
		if (!platform.IsStarted)
		{
			return null;
		}

		var sessions = platform.GetSessions();
		var systemCurrent = platform.GetSystemCurrentAppId();
		lock (_gate)
		{
			var shown = CurrentSessionPicker.Pick(sessions, systemCurrent, _shownLast, _chosen);
			if (cycleInterval > TimeSpan.Zero &&
				string.Equals(shown, _shownLast, StringComparison.Ordinal) &&
				CurrentSessionPicker.Plays(sessions, shown) &&
				time.GetElapsedTime(_shownSince) >= cycleInterval &&
				CurrentSessionPicker.NextPlaying(sessions, shown) is { } next)
			{
				_chosen = next;
				shown = next;
			}

			return Show(shown);
		}
	}

	// Where the shown app stands among the playing ones, when there is more than one.
	internal (int Number, int Count)? CyclePosition(string shown)
	{
		var playing = platform.GetSessions().Where(session => session.IsPlaying).Select(session => session.AppId).ToList();
		var index = playing.FindIndex(appId => string.Equals(appId, shown, StringComparison.Ordinal));
		return playing.Count >= 2 && index >= 0 ? (index + 1, playing.Count) : null;
	}

	internal bool ShowNextPlaying()
	{
		if (!platform.IsStarted)
		{
			return false;
		}

		var sessions = platform.GetSessions();
		var systemCurrent = platform.GetSystemCurrentAppId();
		lock (_gate)
		{
			var shown = CurrentSessionPicker.Pick(sessions, systemCurrent, _shownLast, _chosen);
			if (CurrentSessionPicker.NextPlaying(sessions, shown) is not { } next || string.Equals(next, shown, StringComparison.Ordinal))
			{
				return false;
			}

			_chosen = next;
			Show(next);
			return true;
		}
	}

	private string? Show(string? appId)
	{
		if (appId is not null && !string.Equals(appId, _shownLast, StringComparison.Ordinal))
		{
			_shownLast = appId;
			_shownSince = time.GetTimestamp();
		}

		return appId;
	}
}
