using Format.Fmp4;

namespace Tests.Unit.Fmp4;

[TestFixture]
public class TimestampConverterTests
{
  /// <summary>
  /// SCENARIO:
  /// First timestamp establishes the base, decode time is 0
  ///
  /// ACTION:
  /// Call ToDecodeTime with the first timestamp
  ///
  /// EXPECTED RESULT:
  /// Returns 0
  /// </summary>
  [Test]
  public void FirstTimestamp_ReturnsZero()
  {
    var converter = new TimestampConverter(90000);
    var result = converter.ToDecodeTime(90000);

    Assert.That(result, Is.EqualTo(0UL));
  }

  /// <summary>
  /// SCENARIO:
  /// Second timestamp is 90000 ticks after base (1 second at 90kHz)
  ///
  /// ACTION:
  /// Call ToDecodeTime with base + 90000
  ///
  /// EXPECTED RESULT:
  /// Returns 90000 (direct delta, timestamps are already in timescale units)
  /// </summary>
  [Test]
  public void OneSecondLater_ReturnsTimescale()
  {
    var converter = new TimestampConverter(90000);
    converter.ToDecodeTime(90000);
    var result = converter.ToDecodeTime(180000);

    Assert.That(result, Is.EqualTo(90000UL));
  }

  /// <summary>
  /// SCENARIO:
  /// Calculate duration between two timestamps
  ///
  /// ACTION:
  /// DurationBetween with 3000 tick gap (30fps at 90kHz = 3000 ticks per frame)
  ///
  /// EXPECTED RESULT:
  /// Returns 3000 (direct delta)
  /// </summary>
  [Test]
  public void DurationBetween_CalculatesCorrectly()
  {
    var converter = new TimestampConverter(90000);
    var duration = converter.DurationBetween(0, 3000);

    Assert.That(duration, Is.EqualTo(3000u));
  }

  /// <summary>
  /// SCENARIO:
  /// Reset clears the base timestamp
  ///
  /// ACTION:
  /// Set a base, reset, set a new base
  ///
  /// EXPECTED RESULT:
  /// New base starts from 0 again
  /// </summary>
  [Test]
  public void Reset_ClearsBase()
  {
    var converter = new TimestampConverter(90000);
    converter.ToDecodeTime(90000);
    converter.Reset();
    var result = converter.ToDecodeTime(180000);

    Assert.That(result, Is.EqualTo(0UL));
  }

  /// <summary>
  /// SCENARIO:
  /// RTP timestamp wraps around at 2^32 (~13.3h at 90kHz)
  ///
  /// ACTION:
  /// Feed timestamps approaching uint32 max then crossing to small values
  ///
  /// EXPECTED RESULT:
  /// Decode times continue monotonically across the wrap
  /// </summary>
  [Test]
  public void RtpWrap_ContinuesMonotonically()
  {
    var converter = new TimestampConverter(90000);
    var near_max = 0xFFFF_0000u;

    var t0 = converter.ToDecodeTime(near_max);
    var t1 = converter.ToDecodeTime(near_max + 3000);
    var t2 = converter.ToDecodeTime(near_max + 0x10000u + 3000);

    Assert.That(t1 - t0, Is.EqualTo(3000UL));
    Assert.That(t2 - t1, Is.EqualTo(0x10000UL));
    Assert.That(t2, Is.GreaterThan(t1));
  }

  /// <summary>
  /// SCENARIO:
  /// Multiple wraps over a long-running stream
  ///
  /// ACTION:
  /// Simulate two full wraps
  ///
  /// EXPECTED RESULT:
  /// Decode time reflects the total elapsed ticks across both wraps
  /// </summary>
  [Test]
  public void MultipleWraps_AccumulateCorrectly()
  {
    var converter = new TimestampConverter(90000);
    converter.ToDecodeTime(0);

    converter.ToDecodeTime(0xFFFF_FFFFu);
    var afterFirst = converter.ToDecodeTime(1000);

    Assert.That(afterFirst, Is.EqualTo(0x1_0000_0000UL + 1000));

    converter.ToDecodeTime(0xFFFF_FFFFu);
    var afterSecond = converter.ToDecodeTime(500);

    Assert.That(afterSecond, Is.EqualTo(0x2_0000_0000UL + 500));
  }
}
