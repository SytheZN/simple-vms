using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Client.Core.Decoding;

public sealed class Decoder : IDisposable
{
  private readonly IDecodeBackend _backend;
  private readonly Fetcher _fetcher;
  private readonly ILogger _logger;
  private readonly List<DecodedGop> _gops = [];
  private readonly Dictionary<ulong, int> _decodedChunks = [];
  private DecodedGop? _currentGop;

  private CodecParameters? _codecConfig;
  private uint _timescale = 90000;
  private long _lastWallClockUs;
  private int _stride = 1;
  private int _strideCounter;
  private bool _disposed;

  private readonly Lock _cacheLock = new();
  private readonly Channel<byte> _decodeSignal = Channel.CreateBounded<byte>(
    new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
  private readonly CancellationTokenSource _workerCts = new();
  private readonly Task _decodeWorker;
  private ulong[]? _pendingTargets;
  private readonly Queue<(Action Action, TaskCompletionSource? Tcs)> _commands = new();

  public Decoder(ILogger logger, IDecodeBackend backend, Fetcher fetcher)
  {
    _logger = logger;
    _backend = backend;
    _fetcher = fetcher;
    _decodeWorker = Task.Run(WorkerLoopAsync);
  }

  private async Task WorkerLoopAsync()
  {
    var ct = _workerCts.Token;
    try
    {
      while (await _decodeSignal.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
      {
        while (_decodeSignal.Reader.TryRead(out _)) { }
        if (ct.IsCancellationRequested) break;

        while (true)
        {
          (Action Action, TaskCompletionSource? Tcs) next;
          lock (_cacheLock)
          {
            if (_commands.Count == 0) break;
            next = _commands.Dequeue();
          }
          try
          {
            next.Action();
            next.Tcs?.TrySetResult();
          }
          catch (Exception ex)
          {
            _logger.LogError(ex, "Decoder command failed");
            next.Tcs?.TrySetException(ex);
          }
        }

        ulong[]? targets;
        lock (_cacheLock)
        {
          targets = _pendingTargets;
          _pendingTargets = null;
        }
        if (targets != null) SetTargetInternal(targets);
      }
    }
    catch (OperationCanceledException) { }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Decoder worker crashed");
    }
  }

  private void Signal() => _decodeSignal.Writer.TryWrite(0);

  private void EnqueueCommand(Action action, TaskCompletionSource? tcs)
  {
    lock (_cacheLock) _commands.Enqueue((action, tcs));
    Signal();
  }

  public string BackendDisplayName => _backend.DisplayName;

  public int CachedGopCount
  {
    get { lock (_cacheLock) return _gops.Count + (_currentGop != null ? 1 : 0); }
  }

  public int CachedFrameCount
  {
    get
    {
      lock (_cacheLock)
      {
        var n = 0;
        foreach (var g in _gops) n += g.Frames.Count;
        if (_currentGop != null) n += _currentGop.Frames.Count;
        return n;
      }
    }
  }

  public long NewestFrameTimestampUs
  {
    get
    {
      lock (_cacheLock)
      {
        long best = 0;
        if (_currentGop != null && _currentGop.Frames.Count > 0)
          best = _currentGop.Frames[^1].TimestampUs;
        if (_gops.Count > 0)
        {
          var tail = _gops[^1];
          if (tail.Frames.Count > 0 && tail.Frames[^1].TimestampUs > best)
            best = tail.Frames[^1].TimestampUs;
        }
        return best;
      }
    }
  }

  public void Configure(CodecParameters config) =>
    EnqueueCommand(() => ConfigureInternal(config), null);

  private void ConfigureInternal(CodecParameters config)
  {
    bool sameCodec;
    lock (_cacheLock) sameCodec = SameCodec(_codecConfig, config);
    if (sameCodec)
    {
      FlushForSeekInternal();
      return;
    }
    FlushDecoded();
    lock (_cacheLock) _codecConfig = config;
    _backend.Configure(config);
  }

  public void FlushForSeek() =>
    EnqueueCommand(FlushForSeekInternal, null);

  private void FlushForSeekInternal()
  {
    FlushDecoded();
    _backend.Flush();
    lock (_cacheLock) _lastWallClockUs = 0;
  }

  private static bool SameCodec(CodecParameters? a, CodecParameters? b) =>
    a is not null
    && b is not null
    && a.Codec == b.Codec
    && a.Width == b.Width
    && a.Height == b.Height
    && a.Extradata.AsSpan().SequenceEqual(b.Extradata);

  public void SetTimescale(uint timescale)
  {
    lock (_cacheLock) _timescale = timescale;
  }

  public void SetStride(int newStride) =>
    EnqueueCommand(() => SetStrideInternal(newStride), null);

  private void SetStrideInternal(int newStride)
  {
    CodecParameters? config;
    lock (_cacheLock)
    {
      if (newStride == _stride) return;
      _stride = Math.Max(1, newStride);
      config = _codecConfig;
    }
    FlushDecoded();
    if (config != null) _backend.Configure(config);
  }

  public void ResetWallClock()
  {
    lock (_cacheLock) _lastWallClockUs = 0;
  }

  public void SetTarget(ReadOnlySpan<ulong> gopTimestamps)
  {
    lock (_cacheLock) _pendingTargets = gopTimestamps.ToArray();
    Signal();
  }

  private void SetTargetInternal(ReadOnlySpan<ulong> gopTimestamps)
  {
    var targetSet = new HashSet<ulong>();
    for (var i = 0; i < gopTimestamps.Length; i++)
      targetSet.Add(gopTimestamps[i]);

    var maxKeep = gopTimestamps.Length + 1;
    lock (_cacheLock)
    {
      if (_gops.Count > maxKeep)
      {
        var toRemove = _gops
          .Where(g => !targetSet.Contains(g.Timestamp))
          .Take(_gops.Count - maxKeep)
          .ToList();
        foreach (var gop in toRemove)
        {
          gop.Dispose();
          _gops.Remove(gop);
        }
      }
    }

    for (var i = 0; i < gopTimestamps.Length; i++)
    {
      var gopTs = gopTimestamps[i];
      var gop = _fetcher.FindGop(gopTs);
      if (gop == null || gop.Timestamp != gopTs) continue;

      int decoded;
      var count = gop.Chunks.Count;
      lock (_cacheLock)
        decoded = _decodedChunks.TryGetValue(gopTs, out var n) ? n : 0;
      if (decoded >= count) continue;

      for (var c = decoded; c < count; c++)
        DecodeChunk(gop.Chunks[c], gopTs);
      lock (_cacheLock) _decodedChunks[gopTs] = count;
    }
  }

  // Nearest frame may live just across a GOP boundary, so probe idx+-1 too.
  public DecodedFrame? GetFrame(long ts)
  {
    lock (_cacheLock)
    {
      DecodedFrame? best = null;
      var bestDist = long.MaxValue;

      void Consider(DecodedGop? gop)
      {
        if (gop == null) return;
        var candidate = NearestFrameInGop(gop.Frames, ts);
        if (candidate == null) return;
        var dist = Math.Abs(candidate.TimestampUs - ts);
        if (dist < bestDist)
        {
          bestDist = dist;
          best = candidate;
        }
      }

      if (_gops.Count > 0)
      {
        var idx = FindGopIndexLocked(ts);
        if (idx >= 0) Consider(_gops[idx]);
        else Consider(_gops[0]);
        if (idx + 1 < _gops.Count && idx + 1 >= 0) Consider(_gops[idx + 1]);
        if (idx - 1 >= 0) Consider(_gops[idx - 1]);
      }
      Consider(_currentGop);

      return best;
    }
  }

  private int FindGopIndexLocked(long ts)
  {
    if (_gops.Count == 0) return -1;
    var lo = 0;
    var hi = _gops.Count - 1;
    while (lo < hi)
    {
      var mid = (lo + hi + 1) >>> 1;
      if ((long)_gops[mid].Timestamp <= ts) lo = mid;
      else hi = mid - 1;
    }
    return (long)_gops[lo].Timestamp <= ts ? lo : -1;
  }

  private static DecodedFrame? NearestFrameInGop(List<DecodedFrame> frames, long ts)
  {
    if (frames.Count == 0) return null;
    var lo = 0;
    var hi = frames.Count;
    while (lo < hi)
    {
      var mid = (lo + hi) >>> 1;
      if (frames[mid].TimestampUs < ts) lo = mid + 1;
      else hi = mid;
    }

    DecodedFrame? best = null;
    var bestDist = long.MaxValue;
    if (lo < frames.Count)
    {
      var f = frames[lo];
      var d = Math.Abs(f.TimestampUs - ts);
      if (d < bestDist) { best = f; bestDist = d; }
    }
    if (lo > 0)
    {
      var f = frames[lo - 1];
      var d = Math.Abs(f.TimestampUs - ts);
      if (d < bestDist) { best = f; bestDist = d; }
    }
    return best;
  }

  public Task DecodeKeyframeAsync(ReadOnlyMemory<byte> data, ulong gopTimestamp)
  {
    var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    EnqueueCommand(() => DecodeKeyframeInternal(data, gopTimestamp), tcs);
    return tcs.Task;
  }

  private void DecodeKeyframeInternal(ReadOnlyMemory<byte> data, ulong gopTimestamp)
  {
    uint timescale;
    lock (_cacheLock) timescale = _timescale;
    var samples = Fmp4Demuxer.DemuxGop(data.Span, timescale);
    if (samples.Count == 0) return;

    var key = samples.FirstOrDefault(s => s.IsKey);
    if (key.Data.IsEmpty) return;

    lock (_cacheLock)
    {
      if (key.TimestampUs > 0)
        _lastWallClockUs = key.TimestampUs + key.DurationUs;
      FinalizeCurrentGopLocked();
      _currentGop = new DecodedGop(gopTimestamp);
      _strideCounter = 0;
    }

    _backend.Flush();
    if (!_backend.SendSample(key)) return;
    DrainDecoder();
  }

  private void DecodeChunk(ReadOnlyMemory<byte> data, ulong gopTimestamp)
  {
    uint timescale;
    lock (_cacheLock) timescale = _timescale;
    var samples = Fmp4Demuxer.DemuxGop(data.Span, timescale);
    if (samples.Count == 0) return;

    var hasWallClock = samples[0].TimestampUs > 0;
    lock (_cacheLock)
    {
      if (hasWallClock)
      {
        var last = samples[^1];
        _lastWallClockUs = last.TimestampUs + last.DurationUs;
      }
      else if (_lastWallClockUs > 0)
      {
        // No prft in this chunk: extrapolate PTS/DTS from the previous chunk's
        // end. DTS is set equal to PTS - the real decode order is unknown
        // without prft, but for the common B-frame-free case this matches.
        for (var i = 0; i < samples.Count; i++)
        {
          var s = samples[i];
          samples[i] = s with { TimestampUs = _lastWallClockUs, DecodeTimestampUs = _lastWallClockUs };
          _lastWallClockUs += s.DurationUs;
        }
      }

      if (_currentGop == null || _currentGop.Timestamp != gopTimestamp)
      {
        FinalizeCurrentGopLocked();
        _currentGop = new DecodedGop(gopTimestamp);
        _strideCounter = 0;
      }
    }

    foreach (var sample in samples)
    {
      if (!_backend.SendSample(sample)) continue;
      DrainDecoder();
    }
  }

  private void DrainDecoder()
  {
    var drained = 0;
    while (_backend.TryReceiveFrame(out var frame))
    {
      drained++;
      if (frame == null) continue;

      lock (_cacheLock)
      {
        var keep = _stride <= 1 || (_strideCounter % _stride == 0);
        _strideCounter++;
        if (!keep || _currentGop == null)
        {
          frame.Dispose();
          continue;
        }
        InsertFrameSorted(_currentGop.Frames, frame);
      }
    }
    if (drained > 0 && _logger.IsEnabled(LogLevel.Trace))
      _logger.LogTrace("DrainDecoder: {Drained} frames", drained);
  }

  private static void InsertFrameSorted(List<DecodedFrame> frames, DecodedFrame frame)
  {
    if (frames.Count == 0 || frames[^1].TimestampUs <= frame.TimestampUs)
    {
      frames.Add(frame);
      return;
    }
    var lo = 0;
    var hi = frames.Count;
    while (lo < hi)
    {
      var mid = (lo + hi) >>> 1;
      if (frames[mid].TimestampUs <= frame.TimestampUs) lo = mid + 1;
      else hi = mid;
    }
    frames.Insert(lo, frame);
  }

  private void FinalizeCurrentGopLocked()
  {
    if (_currentGop == null || _currentGop.Frames.Count == 0)
    {
      _currentGop?.Dispose();
      _currentGop = null;
      return;
    }

    var idx = _gops.FindIndex(g => g.Timestamp == _currentGop.Timestamp);
    if (idx >= 0)
    {
      _gops[idx].Dispose();
      _gops[idx] = _currentGop;
    }
    else
    {
      _gops.Add(_currentGop);
      _gops.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
    }
    _currentGop = null;
  }

  private void FlushDecoded()
  {
    lock (_cacheLock)
    {
      foreach (var gop in _gops)
        gop.Dispose();
      _gops.Clear();
      _currentGop?.Dispose();
      _currentGop = null;
      _decodedChunks.Clear();
    }
  }

  public void Dispose()
  {
    if (_disposed) return;
    _disposed = true;
    _workerCts.Cancel();
    _decodeSignal.Writer.TryComplete();
    try { _decodeWorker.Wait(TimeSpan.FromSeconds(1)); } catch { }
    _workerCts.Dispose();
    FlushDecoded();
    _backend.Dispose();
  }

  private sealed class DecodedGop(ulong timestamp) : IDisposable
  {
    public ulong Timestamp { get; } = timestamp;
    public List<DecodedFrame> Frames { get; } = [];

    public void Dispose()
    {
      foreach (var f in Frames)
        f.Dispose();
      Frames.Clear();
    }
  }
}
