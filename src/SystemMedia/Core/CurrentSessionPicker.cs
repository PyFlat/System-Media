namespace SystemMedia.Core;

internal sealed record SessionCandidate(string AppId, bool IsPlaying);

// The system's current app can lag behind (a paused app stays current while another plays, VLC's add-on
// stays current forever), so playing apps win, preferring the user's choice and then the one shown last.
internal static class CurrentSessionPicker
{
	internal static string? Pick(IReadOnlyList<SessionCandidate> sessions, string? windowsCurrent, string? shownLast, string? chosen = null)
	{
		bool Exists(string? appId) =>
			appId is not null && sessions.Any(session => string.Equals(session.AppId, appId, StringComparison.Ordinal));

		if (Plays(sessions, chosen))
		{
			return chosen;
		}

		if (Plays(sessions, windowsCurrent))
		{
			return windowsCurrent;
		}

		if (Plays(sessions, shownLast))
		{
			return shownLast;
		}

		if (sessions.FirstOrDefault(session => session.IsPlaying) is { } playing)
		{
			return playing.AppId;
		}

		if (Exists(windowsCurrent))
		{
			return windowsCurrent;
		}

		return Exists(shownLast) ? shownLast : sessions.Count > 0 ? sessions[0].AppId : null;
	}

	internal static string? NextPlaying(IReadOnlyList<SessionCandidate> sessions, string? shown)
	{
		var playing = sessions.Where(session => session.IsPlaying).Select(session => session.AppId).ToList();
		var index = shown is null ? -1 : playing.FindIndex(appId => string.Equals(appId, shown, StringComparison.Ordinal));
		if (playing.Count == 0 || (index >= 0 && playing.Count == 1))
		{
			return null;
		}

		return playing[(index + 1) % playing.Count];
	}

	internal static bool Plays(IReadOnlyList<SessionCandidate> sessions, string? appId) =>
		appId is not null && sessions.Any(session => session.IsPlaying && string.Equals(session.AppId, appId, StringComparison.Ordinal));
}
