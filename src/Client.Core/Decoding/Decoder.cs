using Microsoft.Extensions.Logging;
using SimpleVms.FFmpeg.Native;

namespace Client.Core.Decoding;

/// <summary>
/// Holds decoded BGRA frames in native memory, indexed by GOP timestamp.
/// Port of src/Client.Web/src/media/decoder.ts: Decoder - owns the codec
/// context, the demux + decode pipeline, stride / lookahead, and the
/// decoded-frame cache.
/// </summary>
public sealed unsafe class Decoder : IDisposable
{
  private readonly VideoDecoder _videoDecoder;
  private readonly Fetcher _fetcher;
  private readonly FrameConverter _converter = new();
  private readonly ILogger _logger;
  private readonly List<DecodedGop> _gops = [];
  private readonly Dictionary<ulong, int> _decodedChunks = [];
  private DecodedGop? _currentGop;

  private CodecParameters? _codecConfig;
  private uint _timescale = 90000;
  private long _lastWallClockUs;
  private int _stride = 1;
  private int _strideCounter;
  private int _captureLogCount;
  private bool _disposed;

  public Decoder(ILogger logger, Fetcher fetcher)
  {
    _logger = logger;
    _fetcher = fetcher;
    _videoDecoder = new VideoDecoder(logger);
  }

  public bool Configure(CodecParameters config)
  {
    if (SameCodec(_codecConfig, config))
    {
      FlushForSeek();
      return true;
    }
    _codecConfig = config;
    FlushDecoded();
    return _videoDecoder.Configure(config);
  }

  /// <summary>
  /// Flush the decoded-frame cache and the codec's internal buffers so the
  /// next SendPacket starts fresh, without rebuilding the codec context.
  /// Used by the Player's seek path when the codec parameters didn't change.
  /// </summary>
  public void FlushForSeek()
  {
    FlushDecoded();
    _videoDecoder.Flush();
    _lastWallClockUs = 0;
  }

  private static bool SameCodec(CodecParameters? a, CodecParameters? b) =>
    a is not null
    && b is not null
    && a.Codec == b.Codec
    && a.Width == b.Width
    && a.Height == b.Height
    && a.Extradata.AsSpan().SequenceEqual(b.Extradata);

  public void SetTimescale(uint timescale) => _timescale = timescale;

  public void SetStride(int newStride)
  {
    if (newStride == _stride) return;
    _stride = Math.Max(1, newStride);
    FlushDecoded();
    if (_codecConfig != null)
      _videoDecoder.Configure(_codecConfig);
  }

  public void ResetWallClock() => _lastWallClockUs = 0;

  /// <summary>
  /// Decodes any pending chunks for the target GOPs (in priority order) and
  /// evicts decoded GOPs that are no longer needed (with a small buffer margin).
  /// Looks up the raw GOP data from the injected Fetcher.
  /// </summary>
  public void SetTarget(ReadOnlySpan<ulong> gopTimestamps)
  {
    var targetSet = new HashSet<ulong>();
    for (var i = 0; i < gopTimestamps.Length; i++)
      targetSet.Add(gopTimestamps[i]);

    var maxKeep = gopTimestamps.Length + 2;
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
        _decodedChunks.Remove(gop.Timestamp);
      }
    }

    for (var i = 0; i < gopTimestamps.Length; i++)
    {
      var gopTs = gopTimestamps[i];
      var gop = _fetcher.FindGop(gopTs);
      if (gop == null || gop.Timestamp != gopTs) continue;

      var decoded = _decodedChunks.TryGetValue(gopTs, out var n) ? n : 0;
      if (decoded >= gop.Chunks.Count) continue;

      for (var c = decoded; c < gop.Chunks.Count; c++)
        DecodeChunk(gop.Chunks[c], gopTs);
      _decodedChunks[gopTs] = gop.Chunks.Count;
    }
  }

  /// <summary>
  /// Returns the decoded frame whose timestamp is closest to ts, or null.
  /// GOPs are sorted by timestamp; frames within a GOP are kept sorted on
  /// insert. Binary-searches to the candidate GOP, then to the candidate
  /// frame, then checks adjacent-GOP edge frames since the nearest frame
  /// can live just across a GOP boundary.
  /// </summary>
  public DecodedFrame? GetFrame(long ts)
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
      var idx = FindGopIndex(ts);
      // idx may be -1 (ts before all GOPs). Probe idx, idx-1 and idx+1.
      if (idx >= 0) Consider(_gops[idx]);
      else Consider(_gops[0]);
      if (idx + 1 < _gops.Count && idx + 1 >= 0) Consider(_gops[idx + 1]);
      if (idx - 1 >= 0) Consider(_gops[idx - 1]);
    }
    Consider(_currentGop);

    return best;
  }

  private int FindGopIndex(long ts)
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
    // First index whose TimestampUs >= ts.
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

  /// <summary>
  /// Single-keyframe decode for scrubbing. Decodes just the keyframe from the
  /// given chunk, discards everything after it. Matches decoder.ts:decodeKeyframe.
  /// </summary>
  public void DecodeKeyframe(ReadOnlyMemory<byte> data, ulong gopTimestamp)
  {
    var samples = Fmp4Demuxer.DemuxGop(data.Span, _timescale);
    if (samples.Count == 0) return;

    var key = samples.FirstOrDefault(s => s.IsKey);
    if (key.Data.IsEmpty) return;

    if (key.TimestampUs > 0)
      _lastWallClockUs = key.TimestampUs + key.DurationUs;

    FinalizeCurrentGop();
    _currentGop = new DecodedGop(gopTimestamp);
    _strideCounter = 0;

    if (!_videoDecoder.SendPacket(key)) return;
    DrainDecoder();
  }

  /// <summary>
  /// Decodes one chunk (moof+mdat) into the current GOP. If this is a new GOP
  /// starts a new DecodedGop; otherwise continues appending to the current one.
  /// </summary>
  private void DecodeChunk(ReadOnlyMemory<byte> data, ulong gopTimestamp)
  {
    var samples = Fmp4Demuxer.DemuxGop(data.Span, _timescale);
    if (samples.Count == 0) return;

    // Fill in wall-clock for samples that didn't carry it (non-Begin chunks)
    var hasWallClock = samples[0].TimestampUs > 0;
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
      FinalizeCurrentGop();
      _currentGop = new DecodedGop(gopTimestamp);
      _strideCounter = 0;
    }

    foreach (var sample in samples)
    {
      if (!_videoDecoder.SendPacket(sample)) continue;
      DrainDecoder();
    }
  }

  private void DrainDecoder()
  {
    var drained = 0;
    while (_videoDecoder.TryReceiveFrame(out var avFrame))
    {
      drained++;
      if (_stride > 1)
      {
        if (_strideCounter % _stride != 0)
        {
          _strideCounter++;
          continue;
        }
        _strideCounter++;
      }
      else
      {
        _strideCounter++;
      }

      CaptureFrame(avFrame);
    }
    if (drained > 0 && _logger.IsEnabled(LogLevel.Trace))
      _logger.LogTrace("DrainDecoder: {Drained} frames", drained);
  }

  private void CaptureFrame(AVFrame* frame)
  {
    if (_currentGop == null) return;

    var w = frame->width;
    var h = frame->height;
    if (w == 0 || h == 0) return;

    var stride = w * 4;
    var size = stride * h;
    var pixels = (nint)FFAvUtil.av_malloc((nuint)size);
    if (pixels == 0) return;

    _converter.Convert(frame, (byte*)pixels, stride);

    const long AV_NOPTS_VALUE = unchecked((long)0x8000000000000000);
    // best_effort_timestamp is the decoder's reordered/recovered pts; for HW
    // decode paths where pts isn't set on output frames this is where the
    // timestamp survives.
    var timestampUs = (long)frame->best_effort_timestamp;
    if (timestampUs == AV_NOPTS_VALUE)
      timestampUs = (long)frame->pts;
    var durationUs = (long)frame->duration;
    if (_logger.IsEnabled(LogLevel.Trace) && _captureLogCount++ % 10 == 0)
      _logger.LogTrace("CaptureFrame pts={Pts} best_effort={Best} duration={Duration}",
        (long)frame->pts, (long)frame->best_effort_timestamp, durationUs);
    // Skip AV_NOPTS_VALUE and zero: without a valid wall-clock the frame
    // can't be looked up by GetFrame anyway (and a literal zero inside a
    // stream whose timestamps are unix-micros is itself a sentinel for
    // "missing").
    if (timestampUs == AV_NOPTS_VALUE || timestampUs <= 0)
    {
      FFAvUtil.av_free((void*)pixels);
      return;
    }
    InsertFrameSorted(_currentGop.Frames,
      new DecodedFrame(timestampUs, durationUs, pixels, w, h, stride));
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

  private void FinalizeCurrentGop()
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
    foreach (var gop in _gops)
      gop.Dispose();
    _gops.Clear();
    _currentGop?.Dispose();
    _currentGop = null;
    _decodedChunks.Clear();
  }

  public void Dispose()
  {
    if (_disposed) return;
    _disposed = true;
    FlushDecoded();
    _converter.Dispose();
    _videoDecoder.Dispose();
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

/// <summary>
/// Refcounted handle to a decoded BGRA frame in native memory. The cache holds
/// the initial reference; anything that takes the frame out of the cache
/// (Player.PublishFrame, BorrowCurrentFrame) must IncrementRef and Dispose to
/// match. The underlying av_malloc buffer is freed when the last ref drops.
/// </summary>
public sealed unsafe class DecodedFrame : IDisposable
{
  private int _refCount = 1;
  private nint _pixels;

  public long TimestampUs { get; }
  public long DurationUs { get; }
  public nint Pixels => _pixels;
  public int Width { get; }
  public int Height { get; }
  public int Stride { get; }

  public DecodedFrame(long timestampUs, long durationUs, nint pixels,
    int width, int height, int stride)
  {
    TimestampUs = timestampUs;
    DurationUs = durationUs;
    _pixels = pixels;
    Width = width;
    Height = height;
    Stride = stride;
  }

  public void IncrementRef() => Interlocked.Increment(ref _refCount);

  public void Dispose()
  {
    if (Interlocked.Decrement(ref _refCount) != 0) return;
    var p = Interlocked.Exchange(ref _pixels, 0);
    if (p != 0) FFAvUtil.av_free((void*)p);
  }
}
