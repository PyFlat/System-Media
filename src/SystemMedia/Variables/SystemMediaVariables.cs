using MacroDeck.Localization;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Variables;

namespace SystemMedia.Variables;

// Variable names are persisted in users' templates.
internal static class SystemMediaVariables
{
	internal const string PositionId = "system-media-position";
	internal const string VolumeId = "system-media-volume";

	private static readonly TimeSpan _fast = TimeSpan.FromSeconds(1);
	private static readonly TimeSpan _normal = TimeSpan.FromSeconds(2);
	private static readonly TimeSpan _slow = TimeSpan.FromSeconds(5);

	private static readonly Entry[] _entries =
	[
		Text("system_media_app_name", Strings.Variables.AppName(), state => state.DeviceName),
		Text("system_media_track_name", Strings.Variables.TrackName(), state => state.TrackName),
		Text("system_media_artist", Strings.Variables.Artist(), state => state.Artists.Count == 0 ? null : string.Join(", ", state.Artists)),
		Text("system_media_album", Strings.Variables.Album(), state => state.AlbumName),
		Text("system_media_playback_state", Strings.Variables.PlaybackState(), state => state.PlaybackState.ToString().ToLowerInvariant()),
		new(VariableDefinition.Eager("system_media_is_playing", VariableType.Boolean, refreshInterval: _normal)
			with { DisplayName = Strings.Variables.IsPlaying() },
			state => state.PlaybackState == PlaybackState.Playing),
		new(VariableDefinition.Eager("system_media_position", VariableType.Numeric, 0, _fast)
			with
			{
				DisplayName = Strings.Variables.Position(),
				SemanticKind = VariableSemanticKinds.Duration,
				Write = MusicPlayerVariableWrites.Position,
			},
			state => state.Position is { } position ? Math.Round(position.TotalSeconds) : null),
		new(VariableDefinition.Eager("system_media_duration", VariableType.Numeric, 0, _normal)
			with { DisplayName = Strings.Variables.Duration(), SemanticKind = VariableSemanticKinds.Duration },
			state => state.Duration is { } duration ? Math.Round(duration.TotalSeconds) : null),
		new(VariableDefinition.Eager("system_media_progress_percentage", VariableType.Numeric, 0, _fast)
			with { DisplayName = Strings.Variables.ProgressPercentage(), Unit = "%", SemanticKind = VariableSemanticKinds.Percentage },
			state => ProgressPercentage(state)),
		new(VariableDefinition.Eager("system_media_volume", VariableType.Numeric, 0, _normal)
			with
			{
				DisplayName = Strings.Variables.Volume(),
				Unit = "%",
				SemanticKind = VariableSemanticKinds.Percentage,
				Write = MusicPlayerVariableWrites.Volume,
			},
			state => state.VolumePercent),
		new(VariableDefinition.Eager("system_media_shuffle_enabled", VariableType.Boolean, refreshInterval: _slow)
			with { DisplayName = Strings.Variables.ShuffleEnabled() },
			state => state.ShuffleEnabled),
		Text("system_media_repeat_mode", Strings.Variables.RepeatMode(), state => state.RepeatMode.ToString().ToLowerInvariant(), _slow),
	];

	internal static IReadOnlyList<VariableDefinition> All { get; } = [.. _entries.Select(entry => entry.Definition)];

	internal static VariableReading Read(string localId, MusicPlayerState state)
	{
		var entry = Array.Find(_entries, candidate => string.Equals(candidate.Definition.ResolvedId, localId, StringComparison.Ordinal));
		if (entry is null)
		{
			return VariableReading.Unavailable;
		}

		var value = entry.Read(state);
		return localId switch
		{
			VolumeId => VariableReading.Of(value, 0, 100, 1),
			PositionId => VariableReading.Of(value, 0, state.Duration?.TotalSeconds, 1),
			_ => VariableReading.Of(value),
		};
	}

	private static int? ProgressPercentage(MusicPlayerState state) =>
		state is { Position: { } position, Duration: { } duration } && duration > TimeSpan.Zero
			? (int)Math.Clamp(Math.Round(position / duration * 100), 0, 100)
			: null;

	private static Entry Text(string name, LocalizedText displayName, Func<MusicPlayerState, object?> read, TimeSpan? refresh = null) =>
		new(VariableDefinition.Eager(name, VariableType.Text, refreshInterval: refresh ?? _normal) with { DisplayName = displayName }, read);

	private sealed record Entry(VariableDefinition Definition, Func<MusicPlayerState, object?> Read);
}
