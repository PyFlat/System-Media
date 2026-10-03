using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;

namespace SystemMedia.Players;

// The UTF-8 message prefixed with its big-endian 32-bit length, answered with "ack".
internal static class QtSingleApplication
{
	private static readonly TimeSpan _acknowledgeTimeout = TimeSpan.FromSeconds(2);

	internal static async Task<bool> SendAsync(string socket, string message, CancellationToken cancellationToken)
	{
		if (LocalSockets.Find([socket]) is not [var endpoint, ..])
		{
			return false;
		}

		try
		{
			await using var stream = await LocalSockets.ConnectAsync(endpoint, cancellationToken);
			var payload = Encoding.UTF8.GetBytes(message);
			var frame = new byte[4 + payload.Length];
			BinaryPrimitives.WriteUInt32BigEndian(frame, (uint)payload.Length);
			payload.CopyTo(frame, 4);
			await stream.WriteAsync(frame, cancellationToken);
			await stream.FlushAsync(cancellationToken);

			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeout.CancelAfter(_acknowledgeTimeout);
			var reply = new byte[3];
			var read = 0;
			while (read < reply.Length && await stream.ReadAsync(reply.AsMemory(read), timeout.Token) is var count and > 0)
			{
				read += count;
			}

			return read == reply.Length && reply.AsSpan().SequenceEqual("ack"u8);
		}
		catch (Exception exception) when (exception is IOException or SocketException or TimeoutException or UnauthorizedAccessException ||
			(exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
		{
			return false;
		}
	}
}
