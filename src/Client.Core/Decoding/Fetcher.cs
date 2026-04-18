namespace Client.Core.Decoding;

public sealed class Fetcher
{
  private readonly List<GopEntry> _gops = [];
  private readonly List<(ulong From, ulong To)> _gaps = [];
  private readonly Lock _lock = new();

  private Func<ulong, ulong, Task>? _sendFetch;
  private bool _fetchInFlight;
  private bool _live;
  private TaskCompletionSource? _fetchTcs;

  public int BufferedGopCount
  {
    get { lock (_lock) return _gops.Count; }
  }

  public int BufferedBytes
  {
    get
    {
      lock (_lock)
      {
        var total = 0;
        foreach (var g in _gops)
          foreach (var c in g.Chunks) total += c.Length;
        return total;
      }
    }
  }

  public void Attach(Func<ulong, ulong, Task> sendFetch)
  {
    _sendFetch = sendFetch;
  }

  public void Detach()
  {
    _sendFetch = null;
  }

  public void SetTarget(long fromUs, long toUs)
  {
    var forward = toUs > fromUs;

    lock (_lock)
    {
      if (_gops.Count > 1)
      {
        var containing = FindGopLocked(fromUs);
        if (containing != null)
        {
          var idx = _gops.IndexOf(containing);
          if (forward && idx > 1)
            _gops.RemoveRange(0, idx - 1);
          else if (!forward && idx < _gops.Count - 2)
            _gops.RemoveRange(idx + 2, _gops.Count - (idx + 2));
        }
      }

      if (_fetchInFlight || _sendFetch == null || _live) return;

      ulong? fetchFrom = null;
      ulong fetchTo = 0;

      if (forward)
      {
        var newest = NewestLocked();
        if (newest == null || (long)newest.Value < toUs)
        {
          var from = newest != null ? newest.Value + 1 : (ulong)fromUs;
          from = SkipGapsForwardLocked(from);
          fetchFrom = from;
          fetchTo = from + 30_000_000;
        }
      }
      else
      {
        var oldest = OldestLocked();
        if (oldest == null || (long)oldest.Value > toUs)
        {
          var from = oldest != null ? oldest.Value - 1 : (ulong)fromUs;
          from = SkipGapsReverseLocked(from);
          fetchFrom = from;
          fetchTo = from >= 30_000_000 ? from - 30_000_000 : 0;
        }
      }

      if (fetchFrom == null) return;
      _fetchInFlight = true;
      _ = _sendFetch(fetchFrom.Value, fetchTo);
    }
  }

  public void HandleFetchComplete()
  {
    TaskCompletionSource? tcs;
    lock (_lock)
    {
      _fetchInFlight = false;
      tcs = _fetchTcs;
      _fetchTcs = null;
    }
    tcs?.TrySetResult();
  }

  public Task FetchAtAsync(ulong ts)
  {
    lock (_lock)
    {
      if (_sendFetch == null) return Task.CompletedTask;
      if (_fetchInFlight)
      {
        var existing = _fetchTcs ??= new TaskCompletionSource();
        return existing.Task;
      }
      _fetchInFlight = true;
      _fetchTcs = new TaskCompletionSource();
      var result = _fetchTcs.Task;
      _ = _sendFetch(ts, ts);
      return result;
    }
  }

  public void HandleLive() { lock (_lock) _live = true; }
  public void HandleRecording() { lock (_lock) _live = false; }

  public void HandleGap(ulong from, ulong to)
  {
    lock (_lock) _gaps.Add((from, to));
  }

  public void AppendData(ulong timestamp, ReadOnlyMemory<byte> chunk)
  {
    lock (_lock)
    {
      var existing = _gops.Find(g => g.Timestamp == timestamp);
      if (existing != null)
      {
        existing.Chunks.Add(chunk);
        return;
      }
      var entry = new GopEntry(timestamp);
      entry.Chunks.Add(chunk);
      _gops.Add(entry);
      _gops.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
    }
  }

  public GopEntry? FindGop(ulong timestamp)
  {
    lock (_lock) return FindGopLocked((long)timestamp);
  }

  public ulong[] GopTimestamps()
  {
    lock (_lock)
    {
      var result = new ulong[_gops.Count];
      for (var i = 0; i < _gops.Count; i++) result[i] = _gops[i].Timestamp;
      return result;
    }
  }

  public ulong? OldestTimestamp()
  {
    lock (_lock) return OldestLocked();
  }

  public ulong? NewestTimestamp()
  {
    lock (_lock) return NewestLocked();
  }

  public void Reset()
  {
    lock (_lock)
    {
      _gops.Clear();
      _gaps.Clear();
      _fetchInFlight = false;
      _live = false;
      _fetchTcs?.TrySetResult();
      _fetchTcs = null;
    }
  }

  public static ReadOnlyMemory<byte> MergedData(GopEntry gop)
  {
    if (gop.Chunks.Count == 1) return gop.Chunks[0];
    var total = 0;
    foreach (var c in gop.Chunks) total += c.Length;
    var merged = new byte[total];
    var off = 0;
    foreach (var c in gop.Chunks) { c.Span.CopyTo(merged.AsSpan(off)); off += c.Length; }
    return merged;
  }

  private GopEntry? FindGopLocked(long timestamp)
  {
    if (_gops.Count == 0) return null;
    var lo = 0;
    var hi = _gops.Count - 1;
    while (lo < hi)
    {
      var mid = (lo + hi + 1) >>> 1;
      if ((long)_gops[mid].Timestamp <= timestamp) lo = mid;
      else hi = mid - 1;
    }
    return (long)_gops[lo].Timestamp <= timestamp ? _gops[lo] : null;
  }

  private ulong? OldestLocked() => _gops.Count > 0 ? _gops[0].Timestamp : null;
  private ulong? NewestLocked() => _gops.Count > 0 ? _gops[^1].Timestamp : null;

  private ulong SkipGapsForwardLocked(ulong ts)
  {
    foreach (var gap in _gaps)
      if (ts >= gap.From && ts < gap.To) ts = gap.To;
    return ts;
  }

  private ulong SkipGapsReverseLocked(ulong ts)
  {
    foreach (var gap in _gaps)
      if (ts > gap.From && ts <= gap.To) ts = gap.From;
    return ts;
  }
}

public sealed class GopEntry(ulong timestamp)
{
  public ulong Timestamp { get; } = timestamp;
  public List<ReadOnlyMemory<byte>> Chunks { get; } = [];
}
