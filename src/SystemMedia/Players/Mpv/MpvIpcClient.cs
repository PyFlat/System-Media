using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace SystemMedia.Players.Mpv;

internal sealed class MpvCommandException(string error) : Exception($"mpv answered: {error}")
{
	internal string Error { get; } = error;
}

// mpv serves every client independently, so this can sit next to a front end's own connection.
internal sealed class MpvIpcClient : IAsyncDisposable
{
	private static readonly TimeSpan _requestTimeout = TimeSpan.FromSeconds(3);

	private readonly Stream _stream;
	private readonly SemaphoreSlim _writeGate = new(1, 1);
	private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
	private readonly CancellationTokenSource _closing = new();
	private readonly Task _reader;
	private long _nextRequestId;

	private MpvIpcClient(Stream stream)
	{
		_stream = stream;
		_reader = Task.Run(ReadLoopAsync);
	}

	internal bool IsConnected => !_reader.IsCompleted;

	internal static async Task<MpvIpcClient> ConnectAsync(string endpoint, CancellationToken cancellationToken) =>
		new(await LocalSockets.ConnectAsync(endpoint, cancellationToken));

	internal async Task<JsonElement?> GetPropertyAsync(string name, CancellationToken cancellationToken)
	{
		try
		{
			var value = await SendAsync(["get_property", name], cancellationToken);
			return value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? null : value;
		}
		catch (MpvCommandException exception) when (exception.Error is "property unavailable" or "property not found")
		{
			return null;
		}
	}

	internal async Task<JsonElement> SendAsync(object[] command, CancellationToken cancellationToken)
	{
		var requestId = Interlocked.Increment(ref _nextRequestId);
		var reply = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
		_pending[requestId] = reply;
		try
		{
			var line = JsonSerializer.Serialize(new Dictionary<string, object> { ["command"] = command, ["request_id"] = requestId }) + "\n";
			var bytes = Encoding.UTF8.GetBytes(line);
			await _writeGate.WaitAsync(cancellationToken);
			try
			{
				await _stream.WriteAsync(bytes, cancellationToken);
				await _stream.FlushAsync(cancellationToken);
			}
			finally
			{
				_writeGate.Release();
			}

			return await reply.Task.WaitAsync(_requestTimeout, cancellationToken);
		}
		finally
		{
			_pending.TryRemove(requestId, out _);
		}
	}

	public async ValueTask DisposeAsync()
	{
		await _closing.CancelAsync();
		await _stream.DisposeAsync();
		await _reader.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
		_closing.Dispose();
		_writeGate.Dispose();
	}

	private async Task ReadLoopAsync()
	{
		try
		{
			using var reader = new StreamReader(_stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
			while (await reader.ReadLineAsync(_closing.Token) is { } line)
			{
				Dispatch(line);
			}
		}
		catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException)
		{
		}
		finally
		{
			foreach (var pending in _pending.Values)
			{
				pending.TrySetException(new IOException("The connection to mpv closed."));
			}
		}
	}

	private void Dispatch(string line)
	{
		JsonDocument document;
		try
		{
			document = JsonDocument.Parse(line);
		}
		catch (JsonException)
		{
			return;
		}

		using (document)
		{
			var root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object ||
				!root.TryGetProperty("request_id", out var id) || !id.TryGetInt64(out var requestId) ||
				!_pending.TryGetValue(requestId, out var reply))
			{
				return;
			}

			var error = root.TryGetProperty("error", out var errorElement) ? errorElement.GetString() : "success";
			if (!string.Equals(error, "success", StringComparison.Ordinal))
			{
				reply.TrySetException(new MpvCommandException(error ?? "unknown error"));
				return;
			}

			reply.TrySetResult(root.TryGetProperty("data", out var data) ? data.Clone() : default);
		}
	}
}
