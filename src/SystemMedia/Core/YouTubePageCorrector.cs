using MacroDeck.Sdk.MusicPlayer;
using Serilog;

namespace SystemMedia.Core;

// Other players on a YouTube page (hover previews) overwrite the browser's media info and never restore it,
// while the page title keeps naming the watched video. Only corrects while that tab is the only one playing.
internal sealed class YouTubePageCorrector : IDisposable
{
	private static readonly TimeSpan _retryFailedLookupAfter = TimeSpan.FromMinutes(1);
	private const int MaxRemembered = 64;

	private readonly Func<string, CancellationToken, Task<YouTubeVideo?>> _lookUp;
	private readonly TimeProvider _time;
	private readonly ILogger _logger;
	private readonly CancellationTokenSource _stopping = new();
	private readonly Lock _gate = new();
	private readonly Dictionary<string, string> _lastPlayingTitle = new(StringComparer.Ordinal);
	private readonly Dictionary<string, YouTubeVideo> _corrections = new(StringComparer.Ordinal);
	private readonly Dictionary<string, string> _videoIds = new(StringComparer.Ordinal);
	private readonly Dictionary<string, Lookup> _lookups = new(StringComparer.Ordinal);

	private volatile bool _enabled;
	private int _disposed;

	internal YouTubePageCorrector(Func<string, CancellationToken, Task<YouTubeVideo?>> lookUp, TimeProvider time, ILogger logger)
	{
		_lookUp = lookUp;
		_time = time;
		_logger = logger.ForContext<YouTubePageCorrector>();
	}

	internal bool Enabled
	{
		get => _enabled;
		set
		{
			_enabled = value;
			if (!value)
			{
				lock (_gate)
				{
					_lastPlayingTitle.Clear();
					_corrections.Clear();
				}
			}
		}
	}

	internal MediaSnapshot Correct(MediaSnapshot snapshot, IReadOnlyList<BrowserTab>? tabs)
	{
		if (!_enabled || tabs is null)
		{
			return snapshot;
		}

		lock (_gate)
		{
			RememberVideoIds(tabs);
			if (PlayingPage(snapshot.AppId, tabs) is not { } page ||
				string.Equals(snapshot.Title?.Trim(), page.VideoTitle, StringComparison.Ordinal))
			{
				_corrections.Remove(snapshot.AppId);
				return snapshot;
			}

			var video = VideoFor(page.VideoTitle);
			if (video is null)
			{
				_corrections.Remove(snapshot.AppId);
			}
			else
			{
				_corrections[snapshot.AppId] = video;
			}

			return snapshot with
			{
				Title = video?.Title ?? page.VideoTitle,
				Artist = video?.Channel,
				AlbumArtist = null,
				AlbumTitle = null,
				HasThumbnail = video?.Thumbnail is not null,
			};
		}
	}

	internal MusicPlayerArtwork? CorrectedThumbnail(string appId)
	{
		lock (_gate)
		{
			return _corrections.GetValueOrDefault(appId)?.Thumbnail;
		}
	}

	// The host disposes the integration once per scope that holds it.
	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) == 0)
		{
			_stopping.Cancel();
			_stopping.Dispose();
		}
	}

	private YouTubePage? PlayingPage(string appId, IReadOnlyList<BrowserTab> tabs)
	{
		var playing = tabs.Where(tab => tab.IsPlaying).ToList();
		if (playing.Count == 1)
		{
			if (YouTubePage.FromTab(playing[0]) is { } page)
			{
				_lastPlayingTitle[appId] = page.VideoTitle;
				return page;
			}

			_lastPlayingTitle.Remove(appId);
			return null;
		}

		if (playing.Count > 0 || !_lastPlayingTitle.TryGetValue(appId, out var lastTitle))
		{
			return null;
		}

		foreach (var tab in tabs)
		{
			if (YouTubePage.FromTab(tab) is { } page && string.Equals(page.VideoTitle, lastTitle, StringComparison.Ordinal))
			{
				return page;
			}
		}

		_lastPlayingTitle.Remove(appId);
		return null;
	}

	// Only the tab in front has a readable address.
	private void RememberVideoIds(IReadOnlyList<BrowserTab> tabs)
	{
		foreach (var tab in tabs)
		{
			if (YouTubePage.FromTab(tab) is { VideoId: { } id } page)
			{
				if (_videoIds.Count >= MaxRemembered && !_videoIds.ContainsKey(page.VideoTitle))
				{
					_videoIds.Clear();
				}

				_videoIds[page.VideoTitle] = id;
			}
		}
	}

	private YouTubeVideo? VideoFor(string videoTitle)
	{
		if (!_videoIds.TryGetValue(videoTitle, out var id))
		{
			return null;
		}

		var now = _time.GetUtcNow();
		if (_lookups.TryGetValue(id, out var lookup))
		{
			if (lookup.Video is { } video)
			{
				// The address can briefly show the next video while the title still names the previous one.
				return string.Equals(video.Title, videoTitle, StringComparison.Ordinal) ? video : null;
			}

			if (lookup.Pending || now - lookup.FailedAt < _retryFailedLookupAfter)
			{
				return null;
			}
		}

		if (_lookups.Count >= MaxRemembered)
		{
			foreach (var finished in _lookups.Where(entry => !entry.Value.Pending).Select(entry => entry.Key).ToList())
			{
				_lookups.Remove(finished);
			}
		}

		_lookups[id] = new Lookup { Pending = true };
		_ = LookUpAsync(id);
		return null;
	}

	private async Task LookUpAsync(string id)
	{
		YouTubeVideo? video = null;
		try
		{
			video = await _lookUp(id, _stopping.Token);
		}
		catch (OperationCanceledException)
		{
			return;
		}
		catch (Exception exception)
		{
			_logger.Warning(exception, "Looking up the YouTube video {VideoId} failed.", id);
		}

		if (video is null)
		{
			_logger.Information("YouTube returned no details for the video {VideoId}; only its title is shown for now.", id);
		}

		lock (_gate)
		{
			_lookups[id] = video is null ? new Lookup { FailedAt = _time.GetUtcNow() } : new Lookup { Video = video };
		}
	}

	private sealed class Lookup
	{
		public bool Pending { get; init; }

		public YouTubeVideo? Video { get; init; }

		public DateTimeOffset FailedAt { get; init; }
	}
}
