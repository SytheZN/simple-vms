using Client.Core.Decoding;

namespace Tests.Unit.Client.Decoding;

[TestFixture]
public class GopPlannerTests
{
  private static readonly ulong[] Available = [10, 20, 30, 40];

  /// <summary>
  /// SCENARIO:
  /// Forward playback at 1x with the playhead inside the second GOP
  ///
  /// ACTION:
  /// ComputeNeededGops(available=[10,20,30,40], ts=25, rate=1, direction=1)
  ///
  /// EXPECTED RESULT:
  /// One GOP behind, the current GOP, and one lookahead GOP: [10, 20, 30]
  /// </summary>
  [Test]
  public void ComputeNeededGops_ForwardRate1_BehindCurrentAndOneAhead()
  {
    var needed = GopPlanner.ComputeNeededGops(Available, 25, 1, 1);

    Assert.That(needed, Is.EqualTo(new ulong[] { 10, 20, 30 }));
  }

  /// <summary>
  /// SCENARIO:
  /// Forward playback at 3x extends the lookahead to three GOPs ahead
  ///
  /// ACTION:
  /// ComputeNeededGops(available=[10,20,30,40], ts=15, rate=3, direction=1)
  ///
  /// EXPECTED RESULT:
  /// Current plus three lookahead GOPs (no behind GOP exists): [10, 20, 30, 40]
  /// </summary>
  [Test]
  public void ComputeNeededGops_ForwardRate3_ExtendsLookahead()
  {
    var needed = GopPlanner.ComputeNeededGops(Available, 15, 3, 1);

    Assert.That(needed, Is.EqualTo(new ulong[] { 10, 20, 30, 40 }));
  }

  /// <summary>
  /// SCENARIO:
  /// Reverse playback at 1x with the playhead inside the second GOP
  ///
  /// ACTION:
  /// ComputeNeededGops(available=[10,20,30,40], ts=25, rate=1, direction=-1)
  ///
  /// EXPECTED RESULT:
  /// The GOP behind the reverse cursor (30), the current GOP, and one GOP back: [30, 20, 10]
  /// </summary>
  [Test]
  public void ComputeNeededGops_Reverse_WalksBackward()
  {
    var needed = GopPlanner.ComputeNeededGops(Available, 25, 1, -1);

    Assert.That(needed, Is.EqualTo(new ulong[] { 30, 20, 10 }));
  }

  /// <summary>
  /// SCENARIO:
  /// The playhead is before the oldest available GOP
  ///
  /// ACTION:
  /// ComputeNeededGops(available=[10,20,30,40], ts=5)
  ///
  /// EXPECTED RESULT:
  /// Empty (no GOP can contain the timestamp)
  /// </summary>
  [Test]
  public void ComputeNeededGops_BeforeOldest_ReturnsEmpty()
  {
    var needed = GopPlanner.ComputeNeededGops(Available, 5, 1, 1);

    Assert.That(needed, Is.Empty);
  }

  /// <summary>
  /// SCENARIO:
  /// No GOPs are available at all
  ///
  /// ACTION:
  /// ComputeNeededGops(available=[], ts=5)
  ///
  /// EXPECTED RESULT:
  /// Empty
  /// </summary>
  [Test]
  public void ComputeNeededGops_NoGops_ReturnsEmpty()
  {
    var needed = GopPlanner.ComputeNeededGops([], 5, 1, 1);

    Assert.That(needed, Is.Empty);
  }
}
