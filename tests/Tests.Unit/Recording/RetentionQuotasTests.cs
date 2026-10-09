using Server.Recording;

namespace Tests.Unit.Recording;

[TestFixture]
public class RetentionQuotasTests
{
  private const long Gb = 1024L * 1024 * 1024;
  private const double GbPerDay = Gb / 86_400.0;
  private const double Tolerance = 0.01 * Gb;

  private static readonly Guid Days30 = Guid.NewGuid();
  private static readonly Guid Size2 = Guid.NewGuid();
  private static readonly Guid PercentA = Guid.NewGuid();
  private static readonly Guid PercentB = Guid.NewGuid();
  private static readonly Guid PercentC = Guid.NewGuid();

  /// <summary>
  /// SCENARIO:
  /// 100 GB usable; every camera records 1 GB/day and holds more than its share:
  /// cam 1 days/30, cam 2 size/2 GB, cams 3 and 4 percent/50, cam 5 percent/30
  ///
  /// ACTION:
  /// Compute(streams, usable)
  ///
  /// EXPECTED RESULT:
  /// Quotas of 30 days, 2 GB, 19.23 GB, 19.23 GB and 11.54 GB; the percent pool is 50 GB
  /// (the largest percent) split by weight, and nothing is scaled because 82 GB fits
  /// </summary>
  [Test]
  public void Compute_MixedPolicies_FitOnDisk()
  {
    var plan = RetentionQuotas.Compute(
    [
      Stream(Days30, RetentionMode.Days, 30, held: 30 * Gb),
      Stream(Size2, RetentionMode.Bytes, 2 * Gb, held: 2 * Gb),
      Stream(PercentA, RetentionMode.Percent, 50, held: 25 * Gb),
      Stream(PercentB, RetentionMode.Percent, 50, held: 25 * Gb),
      Stream(PercentC, RetentionMode.Percent, 30, held: 25 * Gb)
    ], 100 * Gb);

    Assert.That(plan.Scale, Is.EqualTo(1));
    Assert.That(plan.Allowances[Days30].MaxAge, Is.EqualTo(TimeSpan.FromDays(30)));
    Assert.That(plan.Allowances[Size2].MaxBytes, Is.EqualTo(2 * Gb));
    Assert.That((double)plan.Allowances[PercentA].MaxBytes!, Is.EqualTo(19.23 * Gb).Within(Tolerance));
    Assert.That((double)plan.Allowances[PercentB].MaxBytes!, Is.EqualTo(19.23 * Gb).Within(Tolerance));
    Assert.That((double)plan.Allowances[PercentC].MaxBytes!, Is.EqualTo(11.54 * Gb).Within(Tolerance));
  }

  /// <summary>
  /// SCENARIO:
  /// As the mixed example, but cam 3 is percent/80: quotas total 112 GB on a 100 GB disk
  ///
  /// ACTION:
  /// Compute(streams, usable)
  ///
  /// EXPECTED RESULT:
  /// Every quota is scaled by the same factor, 100/112
  /// </summary>
  [Test]
  public void Compute_Overcommitted_ScalesEveryStreamEqually()
  {
    var plan = RetentionQuotas.Compute(
    [
      Stream(Days30, RetentionMode.Days, 30, held: 30 * Gb),
      Stream(Size2, RetentionMode.Bytes, 2 * Gb, held: 2 * Gb),
      Stream(PercentA, RetentionMode.Percent, 80, held: 50 * Gb),
      Stream(PercentB, RetentionMode.Percent, 50, held: 50 * Gb),
      Stream(PercentC, RetentionMode.Percent, 30, held: 50 * Gb)
    ], 100 * Gb);

    var expected = 100.0 / 112;
    Assert.That(plan.Scale, Is.EqualTo(expected).Within(0.001));
    Assert.That(plan.Allowances[Days30].MaxAge!.Value.TotalDays, Is.EqualTo(30 * expected).Within(0.05));
    Assert.That((double)plan.Allowances[Size2].MaxBytes!, Is.EqualTo(2 * Gb * expected).Within(Tolerance));
    Assert.That((double)plan.Allowances[PercentA].MaxBytes!, Is.EqualTo(40 * Gb * expected).Within(Tolerance));
    Assert.That((double)plan.Allowances[PercentB].MaxBytes!, Is.EqualTo(25 * Gb * expected).Within(Tolerance));
    Assert.That((double)plan.Allowances[PercentC].MaxBytes!, Is.EqualTo(15 * Gb * expected).Within(Tolerance));
  }

  /// <summary>
  /// SCENARIO:
  /// Three percent streams share a 50 GB pool; cam 3 has stopped recording and only holds 5 GB
  ///
  /// ACTION:
  /// Compute(streams, usable)
  ///
  /// EXPECTED RESULT:
  /// Cam 3 claims what it holds plus the 1 GB reserve; the remaining 44 GB is split 50:30
  /// between the recording streams
  /// </summary>
  [Test]
  public void Compute_StoppedPercentStream_ReleasesUnclaimedShare()
  {
    var plan = RetentionQuotas.Compute(
    [
      Stream(PercentA, RetentionMode.Percent, 50, held: 5 * Gb),
      Stream(PercentB, RetentionMode.Percent, 50, held: 40 * Gb),
      Stream(PercentC, RetentionMode.Percent, 30, held: 40 * Gb)
    ], 100 * Gb);

    Assert.That((double)plan.Allowances[PercentB].MaxBytes!, Is.EqualTo(27.5 * Gb).Within(Tolerance));
    Assert.That((double)plan.Allowances[PercentC].MaxBytes!, Is.EqualTo(16.5 * Gb).Within(Tolerance));
  }

  /// <summary>
  /// SCENARIO:
  /// A percent stream starts recording again from empty while another fills the pool
  ///
  /// ACTION:
  /// Compute(streams, usable)
  ///
  /// EXPECTED RESULT:
  /// The restarted stream claims only the 1 GB reserve; the other keeps the rest of the pool
  /// and gives it back gradually as the restarted stream grows
  /// </summary>
  [Test]
  public void Compute_RestartedPercentStream_ReclaimsGradually()
  {
    var plan = RetentionQuotas.Compute(
    [
      Stream(PercentA, RetentionMode.Percent, 50, held: 0),
      Stream(PercentB, RetentionMode.Percent, 50, held: 50 * Gb)
    ], 100 * Gb);

    Assert.That((double)plan.Allowances[PercentB].MaxBytes!, Is.EqualTo(49.0 * Gb).Within(Tolerance));
  }

  /// <summary>
  /// SCENARIO:
  /// Two percent streams hold little data, so together they claim less than the 50 GB pool
  ///
  /// ACTION:
  /// Compute(streams, usable)
  ///
  /// EXPECTED RESULT:
  /// Each stream may grow into the whole pool; its own claim caps what it holds each pass
  /// </summary>
  [Test]
  public void Compute_PercentClaimsBelowPool_EachMayGrowToPool()
  {
    var plan = RetentionQuotas.Compute(
    [
      Stream(PercentA, RetentionMode.Percent, 50, held: 2 * Gb),
      Stream(PercentB, RetentionMode.Percent, 30, held: 3 * Gb)
    ], 100 * Gb);

    Assert.That(plan.Allowances[PercentA].MaxBytes, Is.EqualTo(50 * Gb));
    Assert.That(plan.Allowances[PercentB].MaxBytes, Is.EqualTo(50 * Gb));
  }

  /// <summary>
  /// SCENARIO:
  /// The storage cannot report its size
  ///
  /// ACTION:
  /// Compute(streams, null)
  ///
  /// EXPECTED RESULT:
  /// Days and size apply unscaled; percent has no allowance and is flagged unavailable
  /// </summary>
  [Test]
  public void Compute_UnknownStorage_PercentUnavailable()
  {
    var plan = RetentionQuotas.Compute(
    [
      Stream(Days30, RetentionMode.Days, 30, held: 30 * Gb),
      Stream(Size2, RetentionMode.Bytes, 2 * Gb, held: 5 * Gb),
      Stream(PercentA, RetentionMode.Percent, 50, held: 25 * Gb)
    ], null);

    Assert.That(plan.PercentUnavailable, Is.True);
    Assert.That(plan.Scale, Is.EqualTo(1));
    Assert.That(plan.Allowances[Days30].MaxAge, Is.EqualTo(TimeSpan.FromDays(30)));
    Assert.That(plan.Allowances[Size2].MaxBytes, Is.EqualTo(2 * Gb));
    Assert.That(plan.Allowances[PercentA], Is.EqualTo(new StreamAllowance(null, null)));
  }

  /// <summary>
  /// SCENARIO:
  /// Files outside the recorder have consumed all usable space
  ///
  /// ACTION:
  /// Compute(streams, 0)
  ///
  /// EXPECTED RESULT:
  /// Scale is zero; every stream is trimmed to nothing until the emergency stop takes over
  /// </summary>
  [Test]
  public void Compute_NoUsableSpace_ScalesToZero()
  {
    var plan = RetentionQuotas.Compute(
    [
      Stream(Days30, RetentionMode.Days, 30, held: 30 * Gb),
      Stream(Size2, RetentionMode.Bytes, 2 * Gb, held: 2 * Gb)
    ], 0);

    Assert.That(plan.Scale, Is.EqualTo(0).Within(1e-9));
    Assert.That(plan.Allowances[Size2].MaxBytes, Is.EqualTo(0));
    Assert.That(plan.Allowances[Days30].MaxAge!.Value.TotalSeconds, Is.EqualTo(0).Within(1));
  }

  /// <summary>
  /// SCENARIO:
  /// A stream has 36 hours of one-hour segments; the latest 24 hours record at 2 MB/s and
  /// the older 12 hours at 1 MB/s
  ///
  /// ACTION:
  /// MeasureRate(segments)
  ///
  /// EXPECTED RESULT:
  /// The rate covers only the most recent 24 hours of footage
  /// </summary>
  [Test]
  public void MeasureRate_UsesMostRecent24Hours()
  {
    const ulong hour = 3_600_000_000UL;
    var segments = Enumerable.Range(0, 36).Select(i => new Segment
    {
      Id = Guid.NewGuid(),
      StreamId = Guid.Empty,
      StartTime = (ulong)i * hour,
      EndTime = (ulong)(i + 1) * hour,
      SegmentRef = $"ref/{i}",
      SizeBytes = (i >= 12 ? 2_000_000L : 1_000_000L) * 3600,
      KeyframeCount = 1
    });

    Assert.That(RetentionQuotas.MeasureRate(segments), Is.EqualTo(2_000_000).Within(1));
  }

  private static StreamUsage Stream(Guid id, RetentionMode mode, long value, long held) =>
    new(id, mode, value, held, GbPerDay);
}
