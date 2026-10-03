using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.MusicPlayer;

namespace SystemMedia.Events;

// Ids and parameter names are persisted in users' automations.
internal static class MediaEvents
{
	internal const string TrackChangedId = "track-changed";
	internal const string PlaybackStateChangedId = "playback-state-changed";
	internal const string CurrentAppChangedId = "current-app-changed";

	internal const string AppParameter = "app";
	internal const string AppNameParameter = "appName";
	internal const string TrackParameter = "track";
	internal const string ArtistParameter = "artist";
	internal const string AlbumParameter = "album";
	internal const string StateParameter = "state";
	internal const string PreviousAppNameParameter = "previousAppName";

	internal static IReadOnlyList<EventDefinition> All { get; } =
	[
		new EventDefinition
		{
			Id = TrackChangedId,
			Name = Strings.Events.TrackChanged.Name(),
			Description = Strings.Events.TrackChanged.Description(),
			ConfigurationParameters = [AppFilter()],
			PayloadParameters =
			[
				AppPayload(),
				AppNamePayload(),
				ActionParameter.Text(TrackParameter, label: Strings.Events.Parameters.Track()),
				ActionParameter.Text(ArtistParameter, label: Strings.Events.Parameters.Artist()),
				ActionParameter.Text(AlbumParameter, label: Strings.Events.Parameters.Album()),
			],
		},
		new EventDefinition
		{
			Id = PlaybackStateChangedId,
			Name = Strings.Events.PlaybackStateChanged.Name(),
			Description = Strings.Events.PlaybackStateChanged.Description(),
			ConfigurationParameters =
			[
				AppFilter(),
				ActionParameter.Choice(StateParameter, StateOptions(),
					label: Strings.Events.Parameters.State.Label(),
					description: Strings.Events.Parameters.State.Description()),
			],
			PayloadParameters =
			[
				AppPayload(),
				AppNamePayload(),
				ActionParameter.Choice(StateParameter, StateOptions(), label: Strings.Events.Parameters.State.Label()),
			],
		},
		new EventDefinition
		{
			Id = CurrentAppChangedId,
			Name = Strings.Events.CurrentAppChanged.Name(),
			Description = Strings.Events.CurrentAppChanged.Description(),
			ConfigurationParameters = [AppFilter()],
			PayloadParameters =
			[
				AppPayload(),
				AppNamePayload(),
				ActionParameter.Text(PreviousAppNameParameter, label: Strings.Events.Parameters.PreviousAppName()),
			],
		},
	];

	internal static IReadOnlyDictionary<string, object?> TrackChangedPayload(AppObservation app) =>
		new Dictionary<string, object?>
		{
			[AppParameter] = app.InstanceId,
			[AppNameParameter] = app.AppName,
			[TrackParameter] = app.State.TrackName,
			[ArtistParameter] = app.State.Artists.Count == 0 ? null : string.Join(", ", app.State.Artists),
			[AlbumParameter] = app.State.AlbumName,
		};

	internal static IReadOnlyDictionary<string, object?> PlaybackStatePayload(AppObservation app) =>
		new Dictionary<string, object?>
		{
			[AppParameter] = app.InstanceId,
			[AppNameParameter] = app.AppName,
			[StateParameter] = StateValue(app.State.PlaybackState),
		};

	internal static IReadOnlyDictionary<string, object?> CurrentAppPayload(AppObservation app, string? previousAppName) =>
		new Dictionary<string, object?>
		{
			[AppParameter] = app.InstanceId,
			[AppNameParameter] = app.AppName,
			[PreviousAppNameParameter] = previousAppName,
		};

	internal static string StateValue(PlaybackState state) => state.ToString().ToLowerInvariant();

	private static ActionParameter AppFilter() =>
		ActionParameter.DynamicChoice(AppParameter,
			label: Strings.Events.Parameters.App.Label(),
			description: Strings.Events.Parameters.App.Description(),
			placeholder: Strings.Events.Parameters.App.Placeholder());

	private static ActionParameter AppPayload() =>
		ActionParameter.DynamicChoice(AppParameter, label: Strings.Events.Parameters.App.Label());

	private static ActionParameter AppNamePayload() =>
		ActionParameter.Text(AppNameParameter, label: Strings.Events.Parameters.AppName());

	private static IReadOnlyList<ActionParameterOption> StateOptions() =>
	[
		new() { Value = StateValue(PlaybackState.Playing), Label = Strings.Events.Parameters.State.Playing() },
		new() { Value = StateValue(PlaybackState.Paused), Label = Strings.Events.Parameters.State.Paused() },
		new() { Value = StateValue(PlaybackState.Stopped), Label = Strings.Events.Parameters.State.Stopped() },
	];
}
