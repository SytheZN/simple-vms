using Analyzer.MotionGridH26x.Filters;
using Shared.Models.Formats;

namespace Tests.Unit.Motion;

[TestFixture]
public class ThresholdTests
{
  /// <summary>
  /// SCENARIO:
  /// A grid mixes faint activity, the exact threshold, and strong activity
  ///
  /// ACTION:
  /// Push cells at 0, one below the configured threshold, the threshold, and 255
  ///
  /// EXPECTED RESULT:
  /// Values below the threshold are zeroed; the threshold and above pass unchanged
  /// </summary>
  [Test]
  public void FaintCells_AreZeroed()
  {
    var threshold = new Threshold(100);

    var result = threshold.Push(Unit([0, 99, 100, 255], 4, 1));

    Assert.That(result.Data.ToArray(), Is.EqualTo(new byte[] { 0, 0, 100, 255 }));
  }

  /// <summary>
  /// SCENARIO:
  /// The gated frame carries its unit metadata through
  ///
  /// ACTION:
  /// Push a unit with timestamp and sync flag set
  ///
  /// EXPECTED RESULT:
  /// Timestamp, sync flag, and dimensions are preserved
  /// </summary>
  [Test]
  public void GatedFrame_PreservesMetadata()
  {
    var threshold = new Threshold(100);

    var result = threshold.Push(Unit([200, 200], 2, 1, timestamp: 42, sync: true));

    Assert.That(result.Timestamp, Is.EqualTo(42UL));
    Assert.That(result.IsSyncPoint, Is.True);
    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(1));
  }

  private static MotionGridUnit Unit(
    byte[] cells, ushort width, ushort height, ulong timestamp = 0, bool sync = false) => new()
  {
    Data = cells,
    Timestamp = timestamp,
    IsSyncPoint = sync,
    Width = width,
    Height = height
  };
}
