using Client.Core.Controls;

namespace Tests.Unit.Client.Controls;

[TestFixture]
public class TripleTapTrackerTests
{
  private DateTimeOffset _now;
  private Func<DateTimeOffset> Clock => () => _now;

  [SetUp]
  public void SetUp() => _now = DateTimeOffset.UnixEpoch;

  /// <summary>
  /// SCENARIO:
  /// Three taps all occur inside the configured window
  ///
  /// ACTION:
  /// Record three taps with small time steps
  ///
  /// EXPECTED RESULT:
  /// First two return false, third returns true
  /// </summary>
  [Test]
  public void ThreeTapsWithinWindow_TriggersOnThird()
  {
    var tracker = new TripleTapTracker(TimeSpan.FromMilliseconds(1500), Clock);

    Assert.Multiple(() =>
    {
      Assert.That(tracker.Record(), Is.False);
      _now += TimeSpan.FromMilliseconds(100);
      Assert.That(tracker.Record(), Is.False);
      _now += TimeSpan.FromMilliseconds(100);
      Assert.That(tracker.Record(), Is.True);
    });
  }

  /// <summary>
  /// SCENARIO:
  /// Second tap lands after the window has elapsed since the first
  ///
  /// ACTION:
  /// Record a tap, wait past the window, record again
  ///
  /// EXPECTED RESULT:
  /// Second tap restarts the sequence; counter does not reach three
  /// </summary>
  [Test]
  public void TapAfterWindowExpires_RestartsSequence()
  {
    var tracker = new TripleTapTracker(TimeSpan.FromMilliseconds(1500), Clock);

    Assert.Multiple(() =>
    {
      Assert.That(tracker.Record(), Is.False);
      _now += TimeSpan.FromMilliseconds(1501);
      Assert.That(tracker.Record(), Is.False);
      _now += TimeSpan.FromMilliseconds(100);
      Assert.That(tracker.Record(), Is.False);
    });
  }

  /// <summary>
  /// SCENARIO:
  /// Successful triple-tap triggers once, then another triple follows
  ///
  /// ACTION:
  /// Complete a triple, then complete three more taps
  ///
  /// EXPECTED RESULT:
  /// Both sequences trigger; counter resets after each trigger
  /// </summary>
  [Test]
  public void ConsecutiveTriples_BothTrigger()
  {
    var tracker = new TripleTapTracker(TimeSpan.FromMilliseconds(1500), Clock);
    tracker.Record();
    _now += TimeSpan.FromMilliseconds(100);
    tracker.Record();
    _now += TimeSpan.FromMilliseconds(100);
    Assert.That(tracker.Record(), Is.True);

    _now += TimeSpan.FromMilliseconds(100);
    tracker.Record();
    _now += TimeSpan.FromMilliseconds(100);
    tracker.Record();
    _now += TimeSpan.FromMilliseconds(100);
    Assert.That(tracker.Record(), Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// Third tap lands exactly at the window boundary (inclusive)
  ///
  /// ACTION:
  /// Record taps at 0ms, 1ms, 1500ms with window=1500ms
  ///
  /// EXPECTED RESULT:
  /// Third tap triggers (boundary is within the window; check uses greater-than)
  /// </summary>
  [Test]
  public void TapAtExactWindowBoundary_StillTriggers()
  {
    var tracker = new TripleTapTracker(TimeSpan.FromMilliseconds(1500), Clock);
    tracker.Record();
    _now += TimeSpan.FromMilliseconds(1);
    tracker.Record();
    _now += TimeSpan.FromMilliseconds(1499);

    Assert.That(tracker.Record(), Is.True);
  }
}
