using Shared.Models;
using Shared.Models.Entities;

namespace Server.Recording;

internal sealed record StreamUsage(
  Guid StreamId, RetentionMode Mode, long Value, long HeldBytes, double BytesPerSecond);

internal sealed record StreamAllowance(long? MaxBytes, TimeSpan? MaxAge);

internal sealed record QuotaPlan(
  IReadOnlyDictionary<Guid, StreamAllowance> Allowances, double Scale, bool PercentUnavailable);

internal static class RetentionQuotas
{
  public const long ReserveBytes = 1024L * 1024 * 1024;
  public static readonly TimeSpan RateWindow = TimeSpan.FromHours(24);
  private const int BisectIterations = 64;
  private const double SecondsPerDay = 86_400;
  private const double MicrosPerSecond = 1_000_000;

  public static QuotaPlan Compute(IReadOnlyList<StreamUsage> streams, long? usableBytes)
  {
    var targets = Targets(streams, usableBytes);
    var scale = usableBytes is { } usable ? Scale(streams, targets, usable) : 1.0;

    var allowances = streams.ToDictionary(
      s => s.StreamId,
      s => Allowance(s, targets.TryGetValue(s.StreamId, out var t) ? t : null, scale));

    var percentUnavailable = usableBytes == null && streams.Any(s => s.Mode == RetentionMode.Percent);
    return new QuotaPlan(allowances, scale, percentUnavailable);
  }

  public static double MeasureRate(IEnumerable<Segment> segments)
  {
    var windowMicros = RateWindow.TotalSeconds * MicrosPerSecond;
    double bytes = 0;
    double micros = 0;

    foreach (var segment in segments.OrderByDescending(s => s.StartTime))
    {
      if (micros >= windowMicros) break;
      bytes += segment.SizeBytes;
      micros += segment.EndTime - segment.StartTime;
    }

    return micros > 0 ? bytes / (micros / MicrosPerSecond) : 0;
  }

  private static Dictionary<Guid, double> Targets(IReadOnlyList<StreamUsage> streams, long? usableBytes)
  {
    var targets = new Dictionary<Guid, double>();
    foreach (var s in streams)
    {
      if (s.Mode == RetentionMode.Days)
        targets[s.StreamId] = s.BytesPerSecond * s.Value * SecondsPerDay;
      else if (s.Mode == RetentionMode.Bytes)
        targets[s.StreamId] = s.Value;
    }

    if (usableBytes is { } usable)
    {
      var percent = streams.Where(s => s.Mode == RetentionMode.Percent).ToList();
      foreach (var (id, share) in PercentShares(percent, usable))
        targets[id] = share;
    }

    return targets;
  }

  private static IEnumerable<(Guid StreamId, double Share)> PercentShares(
    IReadOnlyList<StreamUsage> streams, long usableBytes)
  {
    if (streams.Count == 0)
      return [];

    var pool = streams.Max(s => s.Value) / 100.0 * Math.Max(usableBytes, 0);
    var claimCaps = streams.Sum(Cap);
    if (claimCaps <= pool)
      return streams.Select(s => (s.StreamId, pool));

    var maxLevel = streams.Max(s => Cap(s) / s.Value);
    var level = LargestWithin(maxLevel, l => streams.Sum(s => Math.Min(s.Value * l, Cap(s))), pool);
    return streams.Select(s => (s.StreamId, s.Value * level));
  }

  private static double Scale(IReadOnlyList<StreamUsage> streams, Dictionary<Guid, double> targets, long usableBytes)
  {
    var claimed = streams.Where(s => targets.ContainsKey(s.StreamId)).ToList();
    double Claims(double scale) => claimed.Sum(s => Math.Min(scale * targets[s.StreamId], Cap(s)));

    return Claims(1) <= usableBytes ? 1 : LargestWithin(1, Claims, usableBytes);
  }

  private static double Cap(StreamUsage s) => s.HeldBytes + (double)ReserveBytes;

  private static double LargestWithin(double upper, Func<double, double> total, double limit)
  {
    double lo = 0;
    var hi = upper;
    for (var i = 0; i < BisectIterations; i++)
    {
      var mid = (lo + hi) / 2;
      if (total(mid) <= limit) lo = mid;
      else hi = mid;
    }
    return lo;
  }

  private static StreamAllowance Allowance(StreamUsage s, double? target, double scale)
  {
    if (s.Mode == RetentionMode.Days)
      return new StreamAllowance(null, TimeSpan.FromDays(s.Value * scale));

    if (target is not { } t)
      return new StreamAllowance(null, null);

    var bytes = t * scale;
    return new StreamAllowance((long)bytes, AgeFor(bytes, s.BytesPerSecond));
  }

  private static TimeSpan? AgeFor(double bytes, double bytesPerSecond)
  {
    if (bytesPerSecond <= 0) return null;
    var seconds = bytes / bytesPerSecond;
    return seconds < TimeSpan.MaxValue.TotalSeconds ? TimeSpan.FromSeconds(seconds) : null;
  }
}
