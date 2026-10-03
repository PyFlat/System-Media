using MacroDeck.Sdk.MusicPlayer;

namespace SystemMedia.Variables;

// The host reads every variable at once; one shared read saves a round trip per variable.
internal sealed class StateSnapshotCache(Func<CancellationToken, Task<MusicPlayerState>> readState, TimeProvider time)
{
	private static readonly TimeSpan _lifetime = TimeSpan.FromMilliseconds(500);

	private readonly Lock _gate = new();
	private Task<MusicPlayerState>? _pending;
	private DateTimeOffset _startedAt;

	internal Task<MusicPlayerState> GetAsync(CancellationToken cancellationToken)
	{
		Task<MusicPlayerState> read;
		lock (_gate)
		{
			var now = time.GetUtcNow();
			if (_pending is null || now - _startedAt >= _lifetime || _pending.IsFaulted || _pending.IsCanceled)
			{
				// The read is shared, so one reader giving up must not fail the others.
				_pending = readState(CancellationToken.None);
				_startedAt = now;
			}

			read = _pending;
		}

		return read.WaitAsync(cancellationToken);
	}
}
