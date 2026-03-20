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
    var result = converter.ToDecodeTime(1_000_000);

    Assert.That(result, Is.EqualTo(0UL));
  }

  /// <summary>
  /// SCENARIO:
  /// Second timestamp is 1 second after base
  ///
  /// ACTION:
  /// Call ToDecodeTime with base + 1_000_000 microseconds
  ///
  /// EXPECTED RESULT:
  /// Returns 90000 (1 second at 90kHz timescale)
  /// </summary>
  [Test]
  public void OneSecondLater_ReturnsTimescale()
  {
    var converter = new TimestampConverter(90000);
    converter.ToDecodeTime(1_000_000);
    var result = converter.ToDecodeTime(2_000_000);

    Assert.That(result, Is.EqualTo(90000UL));
  }

  /// <summary>
  /// SCENARIO:
  /// Calculate duration between two timestamps
  ///
  /// ACTION:
  /// DurationBetween with 33ms gap (30fps frame duration)
  ///
  /// EXPECTED RESULT:
  /// Returns 2970 (33333us * 90000 / 1000000 = 2970 ticks, approximately)
  /// </summary>
  [Test]
  public void DurationBetween_CalculatesCorrectly()
  {
    var converter = new TimestampConverter(90000);
    var duration = converter.DurationBetween(0, 33333);

    Assert.That(duration, Is.EqualTo(2999u));
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
    converter.ToDecodeTime(5_000_000);
    converter.Reset();
    var result = converter.ToDecodeTime(10_000_000);

    Assert.That(result, Is.EqualTo(0UL));
  }
}
