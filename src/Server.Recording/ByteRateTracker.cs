namespace Server.Recording;

public sealed class ByteRateTracker
{
  private const int WindowSize = 20;

  private readonly Lock _lock = new();
  private readonly Dictionary<Guid, Queue<(long Bytes, double Seconds)>> _windows = [];

  public void Record(Guid streamId, long bytes, ulong startTimeMicros, ulong endTimeMicros)
  {
    if (endTimeMicros <= startTimeMicros)
      return;

    var seconds = (endTimeMicros - startTimeMicros) / 1_000_000.0;

    lock (_lock)
    {
      if (!_windows.TryGetValue(streamId, out var queue))
      {
        queue = new Queue<(long, double)>();
        _windows[streamId] = queue;
      }

      queue.Enqueue((bytes, seconds));
      while (queue.Count > WindowSize)
        queue.Dequeue();
    }
  }

  public double GetBytesPerSecond(Guid streamId)
  {
    lock (_lock)
    {
      if (!_windows.TryGetValue(streamId, out var queue) || queue.Count == 0)
        return 0;

      var totalBytes = 0L;
      var totalSeconds = 0.0;
      foreach (var (bytes, seconds) in queue)
      {
        totalBytes += bytes;
        totalSeconds += seconds;
      }

      return totalSeconds > 0 ? totalBytes / totalSeconds : 0;
    }
  }

  public double GetTotalBytesPerSecond()
  {
    lock (_lock)
    {
      var totalRate = 0.0;
      foreach (var streamId in _windows.Keys)
        totalRate += GetBytesPerSecondUnlocked(streamId);
      return totalRate;
    }
  }

  public long EstimateRemainingSeconds(long freeBytes)
  {
    var rate = GetTotalBytesPerSecond();
    if (rate <= 0)
      return -1;
    return (long)(freeBytes / rate);
  }

  private double GetBytesPerSecondUnlocked(Guid streamId)
  {
    if (!_windows.TryGetValue(streamId, out var queue) || queue.Count == 0)
      return 0;

    var totalBytes = 0L;
    var totalSeconds = 0.0;
    foreach (var (bytes, seconds) in queue)
    {
      totalBytes += bytes;
      totalSeconds += seconds;
    }

    return totalSeconds > 0 ? totalBytes / totalSeconds : 0;
  }
}
