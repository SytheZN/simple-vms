namespace Client.Core.Decoding;

public sealed class DecodeController<F> where F : class, IDecodedItem
{
  private readonly Fetcher _fetcher;
  private readonly IChunkDecoder<F> _codec;
  private readonly List<DecodedGop> _gops = [];
  private readonly Dictionary<ulong, int> _decodedChunks = [];

  public DecodeController(Fetcher fetcher, IChunkDecoder<F> codec)
  {
    _fetcher = fetcher;
    _codec = codec;
  }

  public void SetTarget(ulong[] gopTimestamps)
  {
    var targetSet = new HashSet<ulong>(gopTimestamps);
    var maxKeep = gopTimestamps.Length + 2;
    if (_gops.Count > maxKeep)
    {
      var toRemove = _gops
        .Where(g => !targetSet.Contains(g.Timestamp))
        .Take(_gops.Count - maxKeep)
        .ToList();
      foreach (var gop in toRemove)
      {
        foreach (var f in gop.Frames) _codec.Dispose(f);
        _gops.Remove(gop);
        _decodedChunks.Remove(gop.Timestamp);
      }
    }

    foreach (var gopTs in gopTimestamps)
    {
      var gop = _fetcher.FindGop(gopTs);
      if (gop == null || gop.Timestamp != gopTs) continue;

      var count = gop.Chunks.Count;
      var decoded = _decodedChunks.TryGetValue(gopTs, out var n) ? n : 0;
      if (decoded > count)
      {
        _decodedChunks[gopTs] = count;
        continue;
      }
      if (decoded == count) continue;

      for (var i = decoded; i < count; i++)
        _codec.Decode(gop.Chunks[i], gopTs);
      _decodedChunks[gopTs] = count;
    }
  }

  public F? GetFrame(long ts)
  {
    F? best = null;
    var bestDist = long.MaxValue;
    foreach (var gop in _gops)
    {
      foreach (var f in gop.Frames)
      {
        if (f.TimestampUs == 0) continue;
        var dist = Math.Abs(f.TimestampUs - ts);
        if (dist < bestDist)
        {
          bestDist = dist;
          best = f;
        }
      }
    }
    return best;
  }

  public void PushFrame(F frame, ulong gopTimestamp)
  {
    var gop = _gops.Find(g => g.Timestamp == gopTimestamp);
    if (gop == null)
    {
      gop = new DecodedGop(gopTimestamp);
      _gops.Add(gop);
      _gops.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
    }
    gop.Frames.Add(frame);
  }

  public void Clear()
  {
    foreach (var gop in _gops)
      foreach (var f in gop.Frames)
        _codec.Dispose(f);
    _gops.Clear();
    _decodedChunks.Clear();
  }

  public (int Gops, int Frames) Stats()
  {
    var frames = 0;
    foreach (var gop in _gops) frames += gop.Frames.Count;
    return (_gops.Count, frames);
  }

  private sealed class DecodedGop(ulong timestamp)
  {
    public ulong Timestamp { get; } = timestamp;
    public List<F> Frames { get; } = [];
  }
}
