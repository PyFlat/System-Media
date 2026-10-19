using System.Collections.Concurrent;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Variables;
using Serilog;

namespace SystemMedia.Variables;

// Catalog variables reach the host by push, so the subscribed ones are read every second and sent when they change.
internal sealed class AppVariablePublisher(
	Func<IReadOnlyCollection<string>, CancellationToken, Task<IReadOnlyList<VariableValue>>> read,
	ILogger logger) : IDisposable
{
	private static readonly TimeSpan _interval = TimeSpan.FromSeconds(1);

	private readonly ConcurrentDictionary<string, VariableReading> _published = new(StringComparer.Ordinal);
	private readonly FailureEpisodeTracker _failures = new();
	private readonly Lock _gate = new();

	private volatile IVariableSink? _sink;
	private volatile IReadOnlyCollection<string> _subscribed = [];
	private CancellationTokenSource? _stopping;
	private Task? _loop;

	internal IVariableSink? Sink => _sink;

	internal void Start(IVariableSink sink)
	{
		_sink = sink;
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

	// The host receives these values with its subscription, so they count as published.
	internal void Subscribe(IReadOnlyCollection<string> ids, IReadOnlyList<VariableValue> current)
	{
		_subscribed = ids;
		foreach (var id in _published.Keys.Except(ids, StringComparer.Ordinal))
		{
			_published.TryRemove(id, out _);
		}

		foreach (var value in current)
		{
			_published[value.Id] = value.Reading;
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
		var ids = _subscribed;
		if (ids.Count == 0 || _sink is not { } sink)
		{
			return;
		}

		try
		{
			var changed = (await read(ids, cancellationToken))
				.Where(value => !_published.TryGetValue(value.Id, out var last) || last != value.Reading)
				.ToList();
			if (changed.Count > 0)
			{
				await sink.PublishAsync(changed, cancellationToken);
				foreach (var value in changed)
				{
					_published[value.Id] = value.Reading;
				}
			}

			if (_failures.RecordSuccess() is { } episode)
			{
				logger.Information("App variable publisher recovered after {Duration} ({Failures} failed rounds).", episode.Duration, episode.Failures);
			}
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			if (_failures.RecordFailure(exception.Message).Kind == FailureEpisodeSignalKind.Onset)
			{
				logger.Warning(exception, "App variable publisher could not read the media sessions; app variables stay as they are until it can.");
			}
		}
	}
}
