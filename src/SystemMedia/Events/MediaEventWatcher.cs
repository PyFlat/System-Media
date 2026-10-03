using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Logging;
using Serilog;

namespace SystemMedia.Events;

// Polled rather than notification-driven: a notification per changed field would split one track change.
internal sealed class MediaEventWatcher(
	Func<IReadOnlyList<WatchedApp>> apps,
	Func<string?> currentInstanceId,
	ILogger logger) : IDisposable
{
	private static readonly TimeSpan _interval = TimeSpan.FromSeconds(1);

	private readonly MediaChangeDetector _detector = new();
	private readonly FailureEpisodeTracker _failures = new();
	private readonly Lock _gate = new();

	private volatile IEventPublisher? _publisher;
	private CancellationTokenSource? _stopping;
	private Task? _loop;

	internal void Start(IEventPublisher publisher)
	{
		_publisher = publisher;
		lock (_gate)
		{
			if (_loop is not null)
			{
				return;
			}

			_stopping = new CancellationTokenSource();
			_loop = Task.Run(() => RunAsync(_stopping.Token));
		}
	}

	internal async Task StopAsync()
	{
		Task? loop;
		lock (_gate)
		{
			loop = _loop;
			_stopping?.Cancel();
			_loop = null;
		}

		if (loop is not null)
		{
			await loop.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
		}
	}

	public void Dispose()
	{
		lock (_gate)
		{
			_stopping?.Cancel();
			_stopping?.Dispose();
			_stopping = null;
		}
	}

	private async Task RunAsync(CancellationToken cancellationToken)
	{
		using var timer = new PeriodicTimer(_interval);
		try
		{
			while (await timer.WaitForNextTickAsync(cancellationToken))
			{
				await TickAsync(cancellationToken);
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
	}

	private async Task TickAsync(CancellationToken cancellationToken)
	{
		try
		{
			var observations = new List<AppObservation>();
			foreach (var app in apps())
			{
				var state = await app.Player.GetStateAsync(cancellationToken);
				observations.Add(new AppObservation(app.InstanceId, app.DisplayName, state));
			}

			var events = _detector.Observe(observations, currentInstanceId());
			foreach (var mediaEvent in events)
			{
				logger.Information("{Event}: {App} reported {Payload}", mediaEvent.EventId,
					mediaEvent.Payload[MediaEvents.AppNameParameter], string.Join(", ", mediaEvent.Payload.Select(pair => $"{pair.Key}={pair.Value}")));
			}

			if (_publisher is { } publisher)
			{
				foreach (var mediaEvent in events)
				{
					publisher.Publish(mediaEvent.EventId, mediaEvent.Payload);
				}
			}

			if (_failures.RecordSuccess() is { } episode)
			{
				logger.Information("Media event watcher recovered after {Duration} ({Failures} failed rounds).", episode.Duration, episode.Failures);
			}
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			var signal = _failures.RecordFailure(exception.Message);
			switch (signal.Kind)
			{
				case FailureEpisodeSignalKind.Onset:
					logger.Warning(exception, "Media event watcher could not read the media sessions; no events until it can.");
					break;
				case FailureEpisodeSignalKind.SummaryDue:
					logger.Information("Media event watcher still failing after {Duration} ({Failures} rounds): {LastError}",
						signal.Duration, signal.ConsecutiveFailures, signal.LastError);
					break;
				default:
					logger.Debug("Media event watcher round failed: {LastError}", signal.LastError);
					break;
			}
		}
	}
}

internal sealed record WatchedApp(string InstanceId, string DisplayName, MacroDeck.Sdk.MusicPlayer.IMusicPlayer Player);
