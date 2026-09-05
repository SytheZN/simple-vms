namespace Client.Core.Decoding;

public static class GopPlanner
{
  public static ulong[] ComputeNeededGops(ulong[] available, long ts, double rate, int direction)
  {
    var currentIdx = FindGopIndex(available, ts);
    if (currentIdx < 0) return [];

    var lookahead = Math.Max(1, (int)Math.Floor(rate));
    var needed = new List<ulong>();
    var behindIdx = currentIdx - direction;
    if (behindIdx >= 0 && behindIdx < available.Length)
      needed.Add(available[behindIdx]);
    for (var i = 0; i <= lookahead; i++)
    {
      var targetIdx = currentIdx + i * direction;
      if (targetIdx < 0 || targetIdx >= available.Length) break;
      needed.Add(available[targetIdx]);
    }
    return [.. needed];
  }

  public static int FindGopIndex(ulong[] timestamps, long ts)
  {
    if (timestamps.Length == 0) return -1;
    var lo = 0;
    var hi = timestamps.Length - 1;
    while (lo < hi)
    {
      var mid = (lo + hi + 1) >>> 1;
      if ((long)timestamps[mid] <= ts) lo = mid;
      else hi = mid - 1;
    }
    return (long)timestamps[lo] <= ts ? lo : -1;
  }
}
