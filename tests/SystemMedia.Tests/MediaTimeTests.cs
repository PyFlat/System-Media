using NUnit.Framework;
using SystemMedia.Core;

namespace SystemMedia.Tests;

[TestFixture]
public sealed class MediaTimeTests
{
	[Test]
	public void A_reported_time_converts()
	{
		Assert.That(MediaTime.FromMicroseconds(60_000_000), Is.EqualTo(TimeSpan.FromMinutes(1)));
		Assert.That(MediaTime.FromSeconds(90.5), Is.EqualTo(TimeSpan.FromSeconds(90.5)));
		Assert.That(MediaTime.FromSeconds(0), Is.EqualTo(TimeSpan.Zero));
	}

	[TestCase(long.MaxValue)]
	[TestCase(1e300)]
	[TestCase(-1)]
	[TestCase(double.NaN)]
	[TestCase(double.PositiveInfinity)]
	public void A_time_beyond_what_a_timespan_holds_is_unknown(double value)
	{
		Assert.That(MediaTime.FromMicroseconds(value), Is.Null);
		Assert.That(MediaTime.FromSeconds(value), Is.Null);
	}
}
