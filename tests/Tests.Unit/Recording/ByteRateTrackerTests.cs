using Server.Recording;

namespace Tests.Unit.Recording;

[TestFixture]
public class ByteRateTrackerTests
{
  /// <summary>
  /// SCENARIO:
  /// Record 1000 bytes over 1 second (1M microseconds)
  ///
  /// ACTION:
  /// Call GetBytesPerSecond
  ///
  /// EXPECTED RESULT:
  /// Returns 1000.0
  /// </summary>
  [Test]
  public void Record_SingleEntry_CalculatesRate()
  {
    var tracker = new ByteRateTracker();
    var id = Guid.NewGuid();

    tracker.Record(id, 1000, 0, 1_000_000);

    Assert.That(tracker.GetBytesPerSecond(id), Is.EqualTo(1000.0));
  }

  /// <summary>
  /// SCENARIO:
  /// Record two entries: 1000B/1s and 3000B/1s
  ///
  /// ACTION:
  /// Call GetBytesPerSecond
  ///
  /// EXPECTED RESULT:
  /// Returns 2000.0 (4000 bytes / 2 seconds)
  /// </summary>
  [Test]
  public void Record_MultipleEntries_AveragesWindow()
  {
    var tracker = new ByteRateTracker();
    var id = Guid.NewGuid();

    tracker.Record(id, 1000, 0, 1_000_000);
    tracker.Record(id, 3000, 1_000_000, 2_000_000);

    Assert.That(tracker.GetBytesPerSecond(id), Is.EqualTo(2000.0));
  }

  /// <summary>
  /// SCENARIO:
  /// Record 25 entries (window size is 20)
  ///
  /// ACTION:
  /// Call GetBytesPerSecond
  ///
  /// EXPECTED RESULT:
  /// Only the most recent 20 entries are used in the calculation
  /// </summary>
  [Test]
  public void Record_WindowOverflow_EvictsOldest()
  {
    var tracker = new ByteRateTracker();
    var id = Guid.NewGuid();

    for (var i = 0; i < 5; i++)
      tracker.Record(id, 100, (ulong)(i * 1_000_000), (ulong)((i + 1) * 1_000_000));

    for (var i = 5; i < 25; i++)
      tracker.Record(id, 500, (ulong)(i * 1_000_000), (ulong)((i + 1) * 1_000_000));

    Assert.That(tracker.GetBytesPerSecond(id), Is.EqualTo(500.0));
  }

  /// <summary>
  /// SCENARIO:
  /// Record with endTime equal to startTime (zero duration)
  ///
  /// ACTION:
  /// Call Record and then GetBytesPerSecond
  ///
  /// EXPECTED RESULT:
  /// Entry is ignored, rate returns 0
  /// </summary>
  [Test]
  public void Record_ZeroDuration_Ignored()
  {
    var tracker = new ByteRateTracker();
    var id = Guid.NewGuid();

    tracker.Record(id, 1000, 5_000_000, 5_000_000);

    Assert.That(tracker.GetBytesPerSecond(id), Is.EqualTo(0.0));
  }

  /// <summary>
  /// SCENARIO:
  /// No recordings for a given stream ID
  ///
  /// ACTION:
  /// Call GetBytesPerSecond with unknown ID
  ///
  /// EXPECTED RESULT:
  /// Returns 0
  /// </summary>
  [Test]
  public void GetBytesPerSecond_UnknownStream_ReturnsZero()
  {
    var tracker = new ByteRateTracker();

    Assert.That(tracker.GetBytesPerSecond(Guid.NewGuid()), Is.EqualTo(0.0));
  }

  /// <summary>
  /// SCENARIO:
  /// Two streams each recording at 500 B/s
  ///
  /// ACTION:
  /// Call GetTotalBytesPerSecond
  ///
  /// EXPECTED RESULT:
  /// Returns 1000.0
  /// </summary>
  [Test]
  public void GetTotalBytesPerSecond_MultipleStreams_SumsRates()
  {
    var tracker = new ByteRateTracker();
    var id1 = Guid.NewGuid();
    var id2 = Guid.NewGuid();

    tracker.Record(id1, 500, 0, 1_000_000);
    tracker.Record(id2, 500, 0, 1_000_000);

    Assert.That(tracker.GetTotalBytesPerSecond(), Is.EqualTo(1000.0));
  }

  /// <summary>
  /// SCENARIO:
  /// Total rate is 1000 B/s and 5000 bytes free
  ///
  /// ACTION:
  /// Call EstimateRemainingSeconds
  ///
  /// EXPECTED RESULT:
  /// Returns 5
  /// </summary>
  [Test]
  public void EstimateRemainingSeconds_PositiveRate_ReturnsEstimate()
  {
    var tracker = new ByteRateTracker();
    var id = Guid.NewGuid();

    tracker.Record(id, 1000, 0, 1_000_000);

    Assert.That(tracker.EstimateRemainingSeconds(5000), Is.EqualTo(5));
  }

  /// <summary>
  /// SCENARIO:
  /// No recordings (rate is 0)
  ///
  /// ACTION:
  /// Call EstimateRemainingSeconds
  ///
  /// EXPECTED RESULT:
  /// Returns -1 (unknown)
  /// </summary>
  [Test]
  public void EstimateRemainingSeconds_ZeroRate_ReturnsNegativeOne()
  {
    var tracker = new ByteRateTracker();

    Assert.That(tracker.EstimateRemainingSeconds(5000), Is.EqualTo(-1));
  }
}
