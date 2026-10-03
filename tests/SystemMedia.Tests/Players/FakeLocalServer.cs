using System.IO.Pipes;
using System.Net.Sockets;

namespace SystemMedia.Tests.Players;

internal sealed class FakeLocalServer : IAsyncDisposable
{
	private readonly Func<Stream, CancellationToken, Task> _handler;
	private readonly CancellationTokenSource _stopping = new();
	private readonly Socket? _socket;
	private readonly Task _accepting;

	internal FakeLocalServer(string name, Func<Stream, CancellationToken, Task> handler)
	{
		_handler = handler;
		if (OperatingSystem.IsWindows())
		{
			Endpoint = name;
			var first = CreatePipe();
			_accepting = Task.Run(() => AcceptPipesAsync(first));
			return;
		}

		Endpoint = Path.Combine(Path.GetTempPath(), name);
		File.Delete(Endpoint);
		_socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
		_socket.Bind(new UnixDomainSocketEndPoint(Endpoint));
		_socket.Listen();
		_accepting = Task.Run(AcceptSocketsAsync);
	}

	internal string Endpoint { get; }

	internal int Connections { get; private set; }

	public async ValueTask DisposeAsync()
	{
		await _stopping.CancelAsync();
		_socket?.Dispose();
		await _accepting.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
		if (!OperatingSystem.IsWindows())
		{
			File.Delete(Endpoint);
		}

		_stopping.Dispose();
	}

	private NamedPipeServerStream CreatePipe() =>
		new(Endpoint, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

	private async Task AcceptPipesAsync(NamedPipeServerStream first)
	{
		var next = first;
		while (!_stopping.IsCancellationRequested)
		{
			var pipe = next;
			try
			{
				await pipe.WaitForConnectionAsync(_stopping.Token);
			}
			catch
			{
				await pipe.DisposeAsync();
				throw;
			}

			Connections++;
			next = CreatePipe();
			_ = ServeAsync(pipe);
		}
	}

	private async Task AcceptSocketsAsync()
	{
		while (!_stopping.IsCancellationRequested)
		{
			var client = await _socket!.AcceptAsync(_stopping.Token);
			Connections++;
			_ = ServeAsync(new NetworkStream(client, ownsSocket: true));
		}
	}

	private async Task ServeAsync(Stream stream)
	{
		await using (stream)
		{
			try
			{
				await _handler(stream, _stopping.Token);
			}
			catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException)
			{
			}
		}
	}
}
