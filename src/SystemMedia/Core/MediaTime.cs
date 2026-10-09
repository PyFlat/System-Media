namespace SystemMedia.Core;

// Players report lengths they do not know as huge numbers (Chromium sends Int64.MaxValue), beyond what TimeSpan holds.
internal static class MediaTime
{
	private const double MaxMicroseconds = long.MaxValue / TimeSpan.TicksPerMicrosecond;

	internal static TimeSpan? FromMicroseconds(double micros) =>
		micros is >= 0 and < MaxMicroseconds ? TimeSpan.FromMicroseconds(micros) : null;

	internal static TimeSpan? FromSeconds(double seconds) => FromMicroseconds(seconds * 1_000_000);
}
