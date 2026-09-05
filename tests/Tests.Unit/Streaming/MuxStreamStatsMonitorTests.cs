namespace Tests.Unit.Streaming;

[TestFixture]
public class MuxStreamStatsMonitorTests
{
  /// <summary>
  /// SCENARIO:
  /// Frames arrive within the bootstrap window, then time crosses the window boundary
  ///
  /// ACTION:
  /// Record frames then advance the clock past the bootstrap window
  ///
  /// EXPECTED RESULT:
  /// One MuxStreamStats emission carrying observed fps, resolution and average bitrate
  /// </summary>
  [Test]
  public void RecordFrame_AfterBootstrapWindow_EmitsAveragedStats()
  {
    var emitted = new List<MuxStreamStats>();
    var now = new DateTimeOffset(2026, 8, 30, 0, 0, 0, TimeSpan.Zero);
    var monitor = new MuxStreamStatsMonitor(emitted.Add, () => now);

    for (var i = 0; i < 5; i++)
      monitor.RecordFrame("640x480", 1000);

    now += TimeSpan.FromSeconds(10);
    monitor.RecordFrame("640x480", 1000);

    Assert.That(emitted, Has.Count.EqualTo(0));

    now += TimeSpan.FromSeconds(21);
    monitor.RecordFrame("640x480", 1000);

    Assert.That(emitted, Has.Count.EqualTo(1));
    Assert.That(emitted[0].Resolution, Is.EqualTo("640x480"));
    Assert.That(emitted[0].Fps, Is.GreaterThan(0m));
    Assert.That(emitted[0].BitrateKbps, Is.GreaterThan(0));
  }

  /// <summary>
  /// SCENARIO:
  /// After the initial bootstrap emission, further frames fall inside the steady window
  ///
  /// ACTION:
  /// Advance past bootstrap once, then advance past the steady window
  ///
  /// EXPECTED RESULT:
  /// A second emission arrives only after the longer steady window elapses
  /// </summary>
  [Test]
  public void RecordFrame_SecondEmission_WaitsForSteadyWindow()
  {
    var emitted = new List<MuxStreamStats>();
    var now = new DateTimeOffset(2026, 8, 30, 0, 0, 0, TimeSpan.Zero);
    var monitor = new MuxStreamStatsMonitor(emitted.Add, () => now);

    now += TimeSpan.FromSeconds(31);
    monitor.RecordFrame("800x600", 500);
    Assert.That(emitted, Has.Count.EqualTo(1));

    now += TimeSpan.FromMinutes(1);
    monitor.RecordFrame("800x600", 500);
    Assert.That(emitted, Has.Count.EqualTo(1), "Steady window not yet elapsed");

    now += TimeSpan.FromMinutes(5);
    monitor.RecordFrame("800x600", 500);
    Assert.That(emitted, Has.Count.EqualTo(2));
  }

  /// <summary>
  /// SCENARIO:
  /// An empty resolution string comes through the record path
  ///
  /// ACTION:
  /// Record frames alternating between a real resolution and an empty one
  ///
  /// EXPECTED RESULT:
  /// The last non-empty resolution is retained in the emission
  /// </summary>
  [Test]
  public void RecordFrame_EmptyResolution_PreservesLastKnown()
  {
    var emitted = new List<MuxStreamStats>();
    var now = new DateTimeOffset(2026, 8, 30, 0, 0, 0, TimeSpan.Zero);
    var monitor = new MuxStreamStatsMonitor(emitted.Add, () => now);

    monitor.RecordFrame("1280x720", 100);
    monitor.RecordFrame("", 100);
    now += TimeSpan.FromSeconds(31);
    monitor.RecordFrame("", 100);

    Assert.That(emitted[0].Resolution, Is.EqualTo("1280x720"));
  }
}
