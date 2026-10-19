using MacroDeck.Localization;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Variables;
using SystemMedia.Core;
using SystemMedia.Platform;

namespace SystemMedia.Variables;

// Variable names are persisted in users' templates.
internal static class SystemMediaVariables
{
	internal const string PositionId = "system-media-position";
	internal const string VolumeId = "system-media-volume";

	private const string IdPrefix = "system-media-";

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

	// The system's default devices, whatever app is playing. Setting one to a device's name switches to it.
	private static readonly DeviceEntry[] _devices =
	[
		Device("system_media_output_device", Strings.Variables.OutputDevice(), AudioDeviceRole.Output),
		Device("system_media_input_device", Strings.Variables.InputDevice(), AudioDeviceRole.Input),
		Device("system_media_communication_output_device", Strings.Variables.CommunicationOutputDevice(), AudioDeviceRole.CommunicationOutput),
		Device("system_media_communication_input_device", Strings.Variables.CommunicationInputDevice(), AudioDeviceRole.CommunicationInput),
	];

	internal static IReadOnlyList<VariableDefinition> All { get; } =
		[.. _entries.Select(entry => entry.Definition), .. _devices.Select(device => device.Definition)];

	// One string per variable: a localized argument inside another does not reach the host. No app name, the app is in the name.
	private static readonly Dictionary<string, Func<LocalizedText, LocalizedString>> _appDisplayNames = new(StringComparer.Ordinal)
	{
		["system_media_track_name"] = Strings.Variables.App.TrackName,
		["system_media_artist"] = Strings.Variables.App.Artist,
		["system_media_album"] = Strings.Variables.App.Album,
		["system_media_playback_state"] = Strings.Variables.App.PlaybackState,
		["system_media_is_playing"] = Strings.Variables.App.IsPlaying,
		["system_media_position"] = Strings.Variables.App.Position,
		["system_media_duration"] = Strings.Variables.App.Duration,
		["system_media_progress_percentage"] = Strings.Variables.App.ProgressPercentage,
		["system_media_volume"] = Strings.Variables.App.Volume,
		["system_media_shuffle_enabled"] = Strings.Variables.App.ShuffleEnabled,
		["system_media_repeat_mode"] = Strings.Variables.App.RepeatMode,
	};

	private static readonly Entry[] _appEntries = [.. _entries.Where(entry => _appDisplayNames.ContainsKey(entry.Definition.Name!))];

	internal static string AppFolderId(string instanceId) => IdPrefix + instanceId;

	internal static VariableDefinition AppFolder(string instanceId, string appName) =>
		VariableDefinition.OnDemand(AppFolderId(instanceId), VariableType.Text) with
		{
			DisplayName = appName,
			IsContainer = true,
			IsBindable = false,
		};

	// The host keeps the id of a linked variable, not its name, so a name may take the hash only while two apps clash.
	internal static IReadOnlyDictionary<string, string> NameKeys(IEnumerable<(string InstanceId, string AppName)> apps)
	{
		var slugs = apps.Select(app => (app.InstanceId, Slug: InstanceIds.Slug(app.AppName).Replace('-', '_'))).ToList();
		var shared = slugs.GroupBy(app => app.Slug, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
		return slugs.ToDictionary(
			app => app.InstanceId,
			app => app.Slug.Length > 0 && !shared.Contains(app.Slug) ? app.Slug : WithHash(app.Slug, app.InstanceId),
			StringComparer.Ordinal);
	}

	internal static IReadOnlyList<VariableDefinition> ForApp(string instanceId, string appName, string nameKey) =>
	[
		.. _appEntries.Select(entry =>
		{
			var id = AppVariableId(instanceId, entry.Definition.ResolvedId!);
			return entry.Definition with
			{
				Id = id,
				Name = $"system_media_{nameKey}_{entry.Definition.Name!["system_media_".Length..]}",
				Materialization = VariableMaterialization.OnDemand,
				ParentId = AppFolderId(instanceId),
				DisplayName = _appDisplayNames[entry.Definition.Name!](appName),
			};
		}),
	];

	// Every word typed has to appear in the variable's name or its app's name, so "vlc artist" finds VLC's artist.
	internal static bool Matches(VariableDefinition variable, string appName, string search)
	{
		var text = $"{variable.Name} {appName}";
		return search.Split([' ', '_', '.'], StringSplitOptions.RemoveEmptyEntries)
			.All(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));
	}

	// The id of the "any app" variable that an app's variable reads like, or null if it is none of that app's.
	internal static string? CurrentIdOf(string localId, string instanceId)
	{
		var prefix = AppFolderId(instanceId) + "-";
		if (!localId.StartsWith(prefix, StringComparison.Ordinal))
		{
			return null;
		}

		var currentId = IdPrefix + localId[prefix.Length..];
		return Array.Exists(_appEntries, entry => entry.Definition.ResolvedId == currentId) ? currentId : null;
	}

	internal static AudioDeviceRole? DeviceRole(string localId) =>
		Array.Find(_devices, candidate => string.Equals(candidate.Definition.ResolvedId, localId, StringComparison.Ordinal))?.Role;

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

	private static string WithHash(string slug, string instanceId)
	{
		var hash = instanceId[(instanceId.LastIndexOf('-') + 1)..];
		return slug.Length == 0 ? hash : $"{slug}_{hash}";
	}

	private static string AppVariableId(string instanceId, string currentId) =>
		$"{AppFolderId(instanceId)}-{currentId[IdPrefix.Length..]}";

	private sealed record Entry(VariableDefinition Definition, Func<MusicPlayerState, object?> Read);

	private static DeviceEntry Device(string name, LocalizedText displayName, AudioDeviceRole role) =>
		new(VariableDefinition.Eager(name, VariableType.Text, refreshInterval: _slow)
			with { DisplayName = displayName, Write = new VariableWriteCapability() },
			role);

	private sealed record DeviceEntry(VariableDefinition Definition, AudioDeviceRole Role);
}
