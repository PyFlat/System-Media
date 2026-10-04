using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.MusicPlayer.Actions;
using MacroDeck.Sdk.Variables;
using Microsoft.Extensions.Options;
using Serilog;
using SystemMedia.Core;
using SystemMedia.Events;
using SystemMedia.Platform;
using SystemMedia.Players;
using SystemMedia.Variables;

namespace SystemMedia;

public sealed class PluginIntegration
	: IPluginIntegration,
		IMusicPlayerProvider,
		IVariableProvider,
		IEventProvider,
		IDynamicEventOptionsProvider,
		IIntegrationIssueProvider,
		IConfigFlowProvider,
		IDisposable
{
	// Plain strings: MusicPlayerInstance takes no localized text, and the host's picker mixes every provider.
	private const string InstanceNamePrefix = "System Media - ";
	private const string CurrentDisplayName = InstanceNamePrefix + "Any app";

	internal const string ExactYouTubeInfoIssueId = "exact-youtube-info";

	// Option names are the keys in each widget's stored data, so they must never change.
	internal const string CycleOption = "cycle";
	internal const string CycleSecondsOption = "cycle-seconds";
	internal const int MinCycleSeconds = 3;
	internal const int MaxCycleSeconds = 3600;
	private const int DefaultCycleSeconds = 10;

	private readonly CompositeMediaPlatform _platform;
	private readonly CurrentAppSelection _selection;
	private readonly IPluginCatalogNotifier _catalog;
	private readonly KnownAppsStore _store;
	private readonly PluginSettingsStore _settings;
	private readonly ThumbnailHistory _thumbnails;
	private readonly YouTubeVideoLookup _youTubeLookup = new();
	private readonly YouTubePageCorrector _youTube;
	private readonly ILogger _logger;
	private readonly SessionMusicPlayer _current;
	private readonly Dictionary<int, SessionMusicPlayer> _cycling = [];
	private readonly IReadOnlyList<ActionParameter> _currentOptions;
	private readonly StateSnapshotCache _currentState;
	private readonly MediaEventWatcher _events;
	private readonly Lock _appsGate = new();

	private volatile KnownApp[] _apps = [];
	private bool _rememberedAppsLoaded;

	public PluginIntegration(IPluginCatalogNotifier catalog, IOptions<PluginHostOptions> hostOptions, ILogger logger)
	{
		_catalog = catalog;
		_logger = logger.ForContext<PluginIntegration>();
		var dataDirectory = hostOptions.Value.DataDirectory;
#if DEBUG
		// Made-up apps must not be remembered as real ones.
		if (Environment.GetEnvironmentVariable(Demo.DemoMediaPlatform.EnvironmentVariable) is { Length: > 0 })
		{
			dataDirectory = null;
		}
#endif
		_store = new KnownAppsStore(dataDirectory, _logger);
		_settings = new PluginSettingsStore(dataDirectory, _logger);
		_thumbnails = new ThumbnailHistory(dataDirectory, _logger);
		_youTube = new YouTubePageCorrector(_youTubeLookup.LookUpAsync, TimeProvider.System, _logger);
		_platform = new CompositeMediaPlatform(
			MediaPlatformFactory.Create(_logger),
			[
				new PlayersSource(_logger, TimeProvider.System),
			]);
		_selection = new CurrentAppSelection(_platform, TimeProvider.System);
		_current = CreatePlayer(appId: null);
		_currentState = new StateSnapshotCache(_current.GetStateAsync, TimeProvider.System);
		_events = new MediaEventWatcher(WatchedApps, CurrentAppInstanceId, _logger);
		_currentOptions =
		[
			ActionParameter.Toggle(
				CycleOption,
				Strings.MusicPlayer.Options.Cycle.Label(),
				Strings.MusicPlayer.Options.Cycle.Description(),
				defaultValue: false),
			ActionParameter.Number(
				CycleSecondsOption,
				Strings.MusicPlayer.Options.CycleSeconds.Label(),
				Strings.MusicPlayer.Options.CycleSeconds.Description(),
				min: MinCycleSeconds,
				max: MaxCycleSeconds,
				step: 1,
				defaultValue: DefaultCycleSeconds),
		];
		Actions =
		[
			.. MusicPlayerActions.Common(ResolvePlayer, GetInstances),
			new ShowNextPlayingAppAction(_selection.ShowNextPlaying),
			new SetDefaultAudioDeviceAction(() => _platform.AudioDevices, _logger),
		];
	}

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public IReadOnlyList<VariableDefinition> Variables => SystemMediaVariables.All;

	public IReadOnlyList<EventDefinition> EventDefinitions => MediaEvents.All;

	public bool RequiresConfiguration => false;

	// Never false: the host then treats System Media as a single account and lists only its first instance.
	public bool AllowsMultipleConfigurations => true;

	public IConfigFlow CreateConfigFlow() => new SettingsConfigFlow(_youTube.Enabled);

	public async Task InitializeAsync(IIntegrationContext context)
	{
		var changed = LoadRememberedApps();
		ApplyExactYouTubeInfo(await ReadExactYouTubeInfoAsync(context.Config));

		await TryStartPlatformAsync(CancellationToken.None);

		_platform.SessionsChanged -= OnSessionsChanged;
		_platform.SessionsChanged += OnSessionsChanged;

		if (RememberActiveApps() || changed)
		{
			_catalog.CatalogChanged(CapabilityKinds.MusicPlayer, reason: "media apps discovered");
		}

		_events.Start(context.Events);

		_logger.Information("Watching {Platform} media sessions; {AppCount} media app(s) known.",
			_platform.System.GetType().Name, _apps.Length);
	}

	public async Task ShutdownAsync()
	{
		_platform.SessionsChanged -= OnSessionsChanged;
		await _events.StopAsync();
	}

	public void Dispose()
	{
		_platform.SessionsChanged -= OnSessionsChanged;
		_events.Dispose();
		_platform.Dispose();
		_youTube.Dispose();
		_youTubeLookup.Dispose();
		_current.Dispose();
		lock (_cycling)
		{
			foreach (var player in _cycling.Values)
			{
				player.Dispose();
			}
		}

		foreach (var app in _apps)
		{
			app.Player.Dispose();
		}
	}

	public async ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		if (SystemMediaVariables.DeviceRole(localId) is { } role)
		{
			return _platform.AudioDevices is { } devices
				? VariableReading.Of((await devices.GetDefaultAsync(role, cancellationToken))?.Name)
				: VariableReading.Unavailable;
		}

		var state = await _currentState.GetAsync(cancellationToken);
		return SystemMediaVariables.Read(localId, state);
	}

	public ValueTask<VariableWriteResult> SetValueAsync(string localId, object? value, CancellationToken cancellationToken = default) =>
		SystemMediaVariables.DeviceRole(localId) is { } role
			? SetDefaultAudioDeviceAsync(role, value, cancellationToken)
			: localId switch
			{
				SystemMediaVariables.VolumeId => MusicPlayerVariableWrites.SetVolumeAsync(_current, value, cancellationToken),
				SystemMediaVariables.PositionId => MusicPlayerVariableWrites.SeekAsync(_current, value, cancellationToken),
				_ => ValueTask.FromResult(VariableWriteResult.NotWritable()),
			};

	private async ValueTask<VariableWriteResult> SetDefaultAudioDeviceAsync(AudioDeviceRole role, object? value, CancellationToken cancellationToken)
	{
		if (value?.ToString() is not { Length: > 0 } device)
		{
			return VariableWriteResult.InvalidValue();
		}

		return await SetDefaultAudioDeviceAction.SwitchAsync(_platform.AudioDevices, role, device, _logger, cancellationToken) switch
		{
			SetDefaultAudioDeviceAction.Outcome.Switched => VariableWriteResult.Applied(),
			SetDefaultAudioDeviceAction.Outcome.NotSupported => VariableWriteResult.Unavailable(Strings.Actions.SetDefaultAudioDevice.NotSupported()),
			// NotFound would claim the variable itself is missing.
			SetDefaultAudioDeviceAction.Outcome.NotFound => VariableWriteResult.InvalidValue(Strings.Actions.SetDefaultAudioDevice.DeviceNotFound()),
			_ => VariableWriteResult.Failed(Strings.Actions.SetDefaultAudioDevice.Failed()),
		};
	}

	public Task<DynamicOptionsResult> GetEventOptionsAsync(EventOptionsContext context, CancellationToken cancellationToken)
	{
		IReadOnlyList<ActionParameterOption> options = string.Equals(context.ParameterName, MediaEvents.AppParameter, StringComparison.Ordinal)
			? [.. _apps.Select(app => new ActionParameterOption { Value = app.InstanceId, Label = app.DisplayName })]
			: [];

		return Task.FromResult(new DynamicOptionsResult { Options = options });
	}

	public Task<IReadOnlyList<IntegrationIssue>> GetIssuesAsync(CancellationToken cancellationToken = default)
	{
		var issues = _platform.GetIssues();
		return Task.FromResult<IReadOnlyList<IntegrationIssue>>(ExactYouTubeInfoIssue() is { } offer ? [.. issues, offer] : issues);
	}

	public async Task<IssueResolution> ResolveIssueAsync(string issueId, CancellationToken cancellationToken = default)
	{
		var resolution = await _platform.ResolveIssueAsync(issueId, cancellationToken);
		if (resolution is { Success: true } && RememberActiveApps())
		{
			_catalog.CatalogChanged(CapabilityKinds.MusicPlayer, reason: "media issue resolved");
		}

		return resolution ?? IssueResolution.Failed(Strings.Issues.Unknown());
	}

	public IReadOnlyList<MusicPlayerInstance> GetInstances() =>
	[
		new(InstanceIds.Current, CurrentDisplayName) { Options = _currentOptions },
		.. _apps.Select(app => new MusicPlayerInstance(app.InstanceId, InstanceNamePrefix + app.DisplayName)),
	];

	public IMusicPlayer? GetPlayer(string instanceId)
	{
		if (string.Equals(instanceId, InstanceIds.Current, StringComparison.Ordinal))
		{
			return _current;
		}

		foreach (var app in _apps)
		{
			if (string.Equals(app.InstanceId, instanceId, StringComparison.Ordinal))
			{
				return app.Player;
			}
		}

		return null;
	}

	// Display only: the host sends commands to the plain instance, which follows the same shared selection.
	public IMusicPlayer? GetPlayerWithOptions(MusicPlayerOptionsRequest request)
	{
		if (!string.Equals(request.InstanceId, InstanceIds.Current, StringComparison.Ordinal) ||
			CycleSecondsOf(request.Options) is not { } seconds)
		{
			return GetPlayer(request.InstanceId);
		}

		lock (_cycling)
		{
			if (!_cycling.TryGetValue(seconds, out var player))
			{
				player = _cycling[seconds] = CreatePlayer(appId: null, TimeSpan.FromSeconds(seconds));
			}

			return player;
		}
	}

	// null while the widget does not cycle.
	internal static int? CycleSecondsOf(IReadOnlyDictionary<string, object> options) =>
		options.GetValueOrDefault(CycleOption) is true
			? options.GetValueOrDefault(CycleSecondsOption) is double seconds && double.IsFinite(seconds)
				? (int)Math.Clamp(Math.Round(seconds), MinCycleSeconds, MaxCycleSeconds)
				: DefaultCycleSeconds
			: null;

	// Only shown while a browser window shows a YouTube video, so users who never hit the problem are not asked.
	private IntegrationIssue? ExactYouTubeInfoIssue()
	{
		if (_youTube.Enabled ||
			!_platform.GetSessions().Any(session => _platform.GetBrowserWindowTitles(session.AppId).Any(title => YouTubePage.VideoTitleOf(title) is not null)))
		{
			return null;
		}

		return new IntegrationIssue
		{
			Id = ExactYouTubeInfoIssueId,
			Title = Strings.Issues.ExactYouTubeInfo.Title(),
			Description = Strings.Issues.ExactYouTubeInfo.Description(),
			Severity = IntegrationIssueSeverity.Info,
		};
	}

	// The settings page wins once saved; until then the 1.0.x settings file decides, so an earlier opt-in stays on.
	private async Task<bool> ReadExactYouTubeInfoAsync(IIntegrationConfig config)
	{
		try
		{
			foreach (var entry in await config.GetEntriesAsync())
			{
				if (SettingsConfigFlow.ReadFlag(await config.GetStringAsync(entry.Id, SettingsConfigFlow.ExactYouTubeInfoField)) is { } saved)
				{
					return saved;
				}
			}
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			_logger.Warning(exception, "Could not read the System Media settings; using the defaults.");
		}

		return _settings.Load().ExactYouTubeInfo;
	}

	private void ApplyExactYouTubeInfo(bool enabled)
	{
		if (_youTube.Enabled == enabled)
		{
			return;
		}

		_youTube.Enabled = enabled;
		_logger.Information("Exact YouTube info in browsers is {State}.", enabled ? "on" : "off");
		_catalog.CatalogChanged(CapabilityKinds.Issues, reason: "exact YouTube info switched");
	}

	private IReadOnlyList<WatchedApp> WatchedApps() =>
		[.. _apps.Select(app => new WatchedApp(app.InstanceId, app.DisplayName, app.Player))];

	private string? CurrentAppInstanceId() =>
		_selection.Current() is { Length: > 0 } appId ? InstanceIds.ForApp(appId) : null;

	private IMusicPlayer? ResolvePlayer(string? instanceId) =>
		string.IsNullOrEmpty(instanceId) ? _current : GetPlayer(instanceId);

	private void OnSessionsChanged(object? sender, EventArgs e)
	{
		if (RememberActiveApps())
		{
			_catalog.CatalogChanged(CapabilityKinds.MusicPlayer, reason: "media apps changed");
		}
	}

	private async Task TryStartPlatformAsync(CancellationToken cancellationToken)
	{
		try
		{
			await _platform.StartAsync(cancellationToken);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			// A Warning: the user sees it as an issue, and an Error on every re-initialization fails conformance.
			_logger.Warning(exception, "The media sessions of this system are not available; no media session can be read.");
		}
	}

	private bool LoadRememberedApps()
	{
		lock (_appsGate)
		{
			if (_rememberedAppsLoaded)
			{
				return false;
			}

			_rememberedAppsLoaded = true;
			return Merge(_store.Load(), active: false);
		}
	}

	private bool RememberActiveApps()
	{
		lock (_appsGate)
		{
			var active = _platform.GetSessions().Select(session => session.AppId)
				.Distinct(StringComparer.Ordinal)
				.Select(appId => new RememberedApp(appId, Name: null))
				.ToList();
			if (!Merge(active, active: true))
			{
				return false;
			}

			_store.Save([.. _apps.Select(app => new RememberedApp(app.AppId, app.DisplayName))]);
			return true;
		}
	}

	// A name derived from the bare id never replaces a remembered one. Copy-on-write, so readers need no lock.
	private bool Merge(IReadOnlyList<RememberedApp> apps, bool active)
	{
		var merged = _apps.ToList();
		var changed = false;
		foreach (var app in apps)
		{
			var identity = active ? _platform.ResolveApp(app.AppId) : null;
			var name = identity is { IsFallback: false } ? identity.DisplayName : null;
			var index = merged.FindIndex(known => string.Equals(known.AppId, app.AppId, StringComparison.Ordinal));
			if (index >= 0)
			{
				if (name is not null && !string.Equals(merged[index].DisplayName, name, StringComparison.Ordinal))
				{
					merged[index] = merged[index] with { DisplayName = name };
					changed = true;
				}

				continue;
			}

			merged.Add(new KnownApp(
				InstanceIds.ForApp(app.AppId),
				app.AppId,
				name ?? app.Name ?? _platform.ResolveApp(app.AppId).DisplayName,
				CreatePlayer(app.AppId)));
			changed = true;
		}

		if (changed)
		{
			_apps = [.. merged];
		}

		return changed;
	}

	private SessionMusicPlayer CreatePlayer(string? appId, TimeSpan cycleInterval = default) =>
		new(_platform, _selection, appId, TimeProvider.System, _youTube, _thumbnails, cycleInterval);

	private sealed record KnownApp(string InstanceId, string AppId, string DisplayName, SessionMusicPlayer Player);
}
