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
}
