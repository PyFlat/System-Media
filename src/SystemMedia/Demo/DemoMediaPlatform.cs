using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.MusicPlayer;
using SystemMedia.Core;
using SystemMedia.Platform;

namespace SystemMedia.Demo;

// Made-up sessions for store screenshots (make demo, make screenshots). Debug builds only, see SystemMedia.csproj.
internal sealed class DemoMediaPlatform(string folder) : IMediaPlatform
{
	internal const string EnvironmentVariable = "SYSTEM_MEDIA_DEMO";

	// make screenshots writes the scene to show into this file, and reads each scene's name and the app
	// "Any app" should show in it from the other.
	private const string SceneFile = ".scene";
	private const string SceneListFile = ".scenes";

	// Edit freely. Cover and Icon are file names in the demo folder; a missing cover is generated from Hue.
	private static readonly DemoApp _spotify = new("Spotify.exe", "Spotify", Icon: "spotify.png",
	[
		new("Midnight Static", "Neon Harbor", "Afterglow Avenue", TimeSpan.FromSeconds(221), Cover: "afterglow-avenue.png", Hue: 280),
		new("Paper Satellites", "The Quiet Engines", "Low Orbit", TimeSpan.FromSeconds(198), Cover: "low-orbit.png", Hue: 200),
		new("Golden Hour Drive", "Mila Ostrander", "Coastline", TimeSpan.FromSeconds(246), Cover: "coastline.png", Hue: 30),
	]);

	private static readonly DemoApp _firefox = new("Firefox", "Firefox", Icon: "firefox.png",
	[
		new("Building a Synth from Scratch", "Signal Path", null, TimeSpan.FromSeconds(1312), Cover: "synth.png", Hue: 160),
	]);

	private static readonly DemoApp _vlc = new("vlc.exe", "VLC media player", Icon: "vlc.png",
	[
		new("Rainy Window", "Lofi Collective", "Slow Mornings", TimeSpan.FromSeconds(174), Cover: "slow-mornings.png", Hue: 220),
	]);

	// make screenshots captures every scene in turn, once "Any app" shows the first playing app of it (a
	// cycling widget gets there within one round); make demo alone shows the first scene.
	private static readonly DemoScene[] _scenes =
	[
		new("several-apps", [
			new(_spotify, Playing: true, Progress: 0.37, Volume: 72, Shuffle: true, Repeat: RepeatMode.Context),
			new(_firefox, Playing: true, Progress: 0.52, Volume: 55),
			new(_vlc, Playing: true, Progress: 0.2, Volume: 40),
		]),
		new("firefox-paused", [new(_firefox, Playing: false, Progress: 0.52, Volume: 55)]),
		new("vlc", [new(_vlc, Playing: true, Progress: 0.2, Volume: 40)]),
	];

	private static readonly TimeSpan _sceneCheckInterval = TimeSpan.FromMilliseconds(250);

	private readonly Lock _gate = new();
	private Dictionary<string, AppState> _states = StatesOf(_scenes[0]);
	private string _scene = _scenes[0].Name;
	private Timer? _sceneTimer;

	public event EventHandler? SessionsChanged;

	public bool IsStarted => true;

	public Task StartAsync(CancellationToken cancellationToken)
	{
		if (_sceneTimer is null)
		{
			File.WriteAllLines(Path.Combine(folder, SceneListFile), _scenes.Select(scene => $"{scene.Name}	{scene.Shown}"));
			File.Delete(Path.Combine(folder, SceneFile));
			_sceneTimer = new Timer(_ => CheckScene(), null, _sceneCheckInterval, _sceneCheckInterval);
		}

		return Task.CompletedTask;
	}

	public IReadOnlyList<SessionCandidate> GetSessions()
	{
		lock (_gate)
		{
			return [.. _states.Values.Select(state => new SessionCandidate(state.App.AppId, state.Playing))];
		}
	}

	public string? GetSystemCurrentAppId()
	{
		lock (_gate)
		{
			return _states.Values.FirstOrDefault()?.App.AppId;
		}
	}

	public Task<MediaSnapshot?> ReadAsync(string appId, CancellationToken cancellationToken)
	{
		lock (_gate)
		{
			if (!_states.TryGetValue(appId, out var state))
			{
				return Task.FromResult<MediaSnapshot?>(null);
			}

			var track = state.Track;
			return Task.FromResult<MediaSnapshot?>(new MediaSnapshot
			{
				AppId = appId,
				AppName = state.App.Name,
				Title = track.Title,
				Artist = track.Artist,
				AlbumTitle = track.Album,
				HasThumbnail = true,
				Status = state.Playing ? PlaybackState.Playing : PlaybackState.Paused,
				PlaybackRate = 1,
				IsShuffleActive = state.Shuffle,
				RepeatMode = state.Repeat,
				EndTime = track.Duration,
				Position = state.Position,
				LastUpdatedTime = state.PositionAt,
			});
		}
	}

	public async Task<MusicPlayerArtwork?> ReadThumbnailAsync(string appId, CancellationToken cancellationToken)
	{
		DemoTrack track;
		lock (_gate)
		{
			if (!_states.TryGetValue(appId, out var state))
			{
				return null;
			}

			track = state.Track;
		}

		return await LoadAsync(track.Cover, cancellationToken) ?? new MusicPlayerArtwork(GradientCover.Png(track.Hue), "image/png");
	}

	public Task<bool> SendAsync(string appId, MediaCommand command, CancellationToken cancellationToken) =>
		Change(appId, state =>
		{
			switch (command)
			{
				case MediaCommand.Play:
					state.Seek(state.CurrentPosition, playing: true);
					break;
				case MediaCommand.Pause:
					state.Seek(state.CurrentPosition, playing: false);
					break;
				case MediaCommand.TogglePlayPause:
					state.Seek(state.CurrentPosition, !state.Playing);
					break;
				case MediaCommand.Next or MediaCommand.Previous:
					var count = state.App.Tracks.Count;
					state.TrackIndex = (state.TrackIndex + (command == MediaCommand.Next ? 1 : count - 1)) % count;
					state.Seek(TimeSpan.Zero, state.Playing);
					break;
			}
		});

	public Task<bool> SeekAsync(string appId, TimeSpan position, CancellationToken cancellationToken) =>
		Change(appId, state => state.Seek(position, state.Playing));

	public Task<bool> SetShuffleAsync(string appId, bool enabled, CancellationToken cancellationToken) =>
		Change(appId, state => state.Shuffle = enabled);

	public Task<bool> SetRepeatModeAsync(string appId, RepeatMode mode, CancellationToken cancellationToken) =>
		Change(appId, state => state.Repeat = mode);

	// Every app of every scene, so instances keep their names while a scene leaves their app out.
	public AppIdentity ResolveApp(string appId) =>
		new(appId, AppOf(appId)?.Name ?? appId, ExecutablePath: null, PackageFamilyName: null);

	public bool HasAppIcon(AppIdentity app) =>
		AppOf(app.AppId)?.Icon is { } icon && File.Exists(Path.Combine(folder, icon));

	public Task<MusicPlayerArtwork?> GetAppIconAsync(AppIdentity app, CancellationToken cancellationToken) =>
		LoadAsync(AppOf(app.AppId)?.Icon, cancellationToken);

	public int? GetVolumePercent(AppIdentity app)
	{
		lock (_gate)
		{
			return _states.TryGetValue(app.AppId, out var state) ? state.Volume : null;
		}
	}

	public Task<bool> SetVolumePercentAsync(AppIdentity app, int volumePercent, CancellationToken cancellationToken) =>
		Change(app.AppId, state => state.Volume = Math.Clamp(volumePercent, 0, 100));

	public IReadOnlyList<IntegrationIssue> GetIssues() => [];

	public Task<IssueResolution?> ResolveIssueAsync(string issueId, CancellationToken cancellationToken) =>
		Task.FromResult<IssueResolution?>(null);

	public void Dispose() => _sceneTimer?.Dispose();

	private static DemoApp? AppOf(string appId) =>
		_scenes.SelectMany(scene => scene.Sessions).Select(session => session.App)
			.FirstOrDefault(app => string.Equals(app.AppId, appId, StringComparison.Ordinal));

	private static Dictionary<string, AppState> StatesOf(DemoScene scene) =>
		scene.Sessions.ToDictionary(
			session => session.App.AppId,
			session => new AppState(session.App, session.Track)
			{
				Playing = session.Playing,
				Position = session.App.Tracks[session.Track].Duration * session.Progress,
				Volume = session.Volume,
				Shuffle = session.Shuffle,
				Repeat = session.Repeat,
			},
			StringComparer.Ordinal);

	private void CheckScene()
	{
		string? requested;
		try
		{
			var path = Path.Combine(folder, SceneFile);
			requested = File.Exists(path) ? File.ReadAllText(path).Trim() : _scenes[0].Name;
		}
		catch (IOException)
		{
			return;
		}

		lock (_gate)
		{
			if (string.Equals(requested, _scene, StringComparison.Ordinal) ||
				Array.Find(_scenes, scene => string.Equals(scene.Name, requested, StringComparison.Ordinal)) is not { } scene)
			{
				return;
			}

			_scene = scene.Name;
			_states = StatesOf(scene);
		}

		SessionsChanged?.Invoke(this, EventArgs.Empty);
	}

	private Task<bool> Change(string appId, Action<AppState> change)
	{
		lock (_gate)
		{
			if (!_states.TryGetValue(appId, out var state))
			{
				return Task.FromResult(false);
			}

			change(state);
		}

		SessionsChanged?.Invoke(this, EventArgs.Empty);
		return Task.FromResult(true);
	}

	private async Task<MusicPlayerArtwork?> LoadAsync(string? fileName, CancellationToken cancellationToken)
	{
		var path = fileName is null ? null : Path.Combine(folder, fileName);
		if (path is null || !File.Exists(path))
		{
			return null;
		}

		var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
		return ArtworkFormat.IsImage(bytes) ? new MusicPlayerArtwork(bytes, ArtworkFormat.SniffContentType(bytes)) : null;
	}

	private sealed record DemoApp(string AppId, string Name, string? Icon, IReadOnlyList<DemoTrack> Tracks);

	private sealed record DemoTrack(string Title, string Artist, string? Album, TimeSpan Duration, string? Cover, int Hue);

	// Progress is how far into the track it starts, from 0 to 1.
	private sealed record DemoSession(
		DemoApp App,
		bool Playing,
		double Progress,
		int Volume,
		int Track = 0,
		bool Shuffle = false,
		RepeatMode Repeat = RepeatMode.Off);

	private sealed record DemoScene(string Name, IReadOnlyList<DemoSession> Sessions)
	{
		internal string Shown => (Sessions.FirstOrDefault(session => session.Playing) ?? (Sessions.Count > 0 ? Sessions[0] : null))?.App.Name ?? string.Empty;
	}

	private sealed class AppState(DemoApp app, int trackIndex)
	{
		internal DemoApp App { get; } = app;

		internal int TrackIndex { get; set; } = trackIndex;

		internal DemoTrack Track => App.Tracks[TrackIndex];

		internal bool Playing { get; set; }

		internal int Volume { get; set; }

		internal bool Shuffle { get; set; }

		internal RepeatMode Repeat { get; set; }

		// The mapper projects a playing position forward from PositionAt, so progress moves on its own.
		internal TimeSpan Position { get; set; }

		internal DateTimeOffset PositionAt { get; private set; } = DateTimeOffset.UtcNow;

		internal TimeSpan CurrentPosition =>
			Playing ? TimeSpan.FromTicks(Math.Min((Position + (DateTimeOffset.UtcNow - PositionAt)).Ticks, Track.Duration.Ticks)) : Position;

		internal void Seek(TimeSpan position, bool playing)
		{
			Position = position;
			PositionAt = DateTimeOffset.UtcNow;
			Playing = playing;
		}
	}
}
