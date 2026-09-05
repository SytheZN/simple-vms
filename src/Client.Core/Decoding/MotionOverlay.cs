using Client.Core.Decoding.Diagnostics;
using Client.Core.Streaming;
using Microsoft.Extensions.Logging;
using Shared.Protocol;

namespace Client.Core.Decoding;

public sealed class MotionOverlay : IDisposable
{
  private const long HoldLimitUs = 5_000_000;
  private const long WindowBaseUs = 30_000_000;

  private readonly ILogger _logger;
  private readonly Fetcher _fetcher = new();
  private readonly MotionDecoder _decoder;
  private readonly Lock _lock = new();

  private IVideoFeed? _feed;
  private OverlayPlayerView _view;
  private long _lastPaintedTs = -1;

  public event Action<MotionFrame?>? FrameChanged;

  public MotionOverlay(ILogger logger)
  {
    _logger = logger;
    _decoder = new MotionDecoder(logger, _fetcher);
  }

  public void AttachFeed(IVideoFeed feed)
  {
    lock (_lock)
    {
      DetachFeedLocked();
      _feed = feed;
      feed.OnGop += HandleGop;
      feed.OnStatus += HandleStatus;
      feed.OnGap += HandleGap;
      _fetcher.Attach((from, to) => feed.SendFetchAsync(from, to, CancellationToken.None));
      feed.Start();
    }
  }

  public void DetachFeed()
  {
    lock (_lock) DetachFeedLocked();
    FrameChanged?.Invoke(null);
  }

  public void OnSeek()
  {
    lock (_lock) ResetLocked();
    FrameChanged?.Invoke(null);
  }

  public void Tick(OverlayPlayerView view)
  {
    bool changed;
    MotionFrame? frame;
    lock (_lock)
    {
      _view = view;
      changed = TickLocked(out frame);
    }
    if (changed) FrameChanged?.Invoke(frame);
  }

  public PipelineStats BuildPipelineStats(string profile)
  {
    lock (_lock)
    {
      var d = _decoder.Stats();
      var gops = _fetcher.GopTimestamps();
      var pos = _view.TimestampUs;
      var bufferUs = gops.Length > 0 ? Math.Max(0, (long)gops[^1] - pos) : 0L;
      return new PipelineStats("overlay", profile, bufferUs, pos,
        _fetcher.BufferedGopCount, _fetcher.BufferedBytes, d.Gops, d.Frames);
    }
  }

  public void Dispose() => DetachFeed();

  private void DetachFeedLocked()
  {
    if (_feed == null) return;
    _feed.OnGop -= HandleGop;
    _feed.OnStatus -= HandleStatus;
    _feed.OnGap -= HandleGap;
    _feed = null;
    _fetcher.Detach();
    ResetLocked();
  }

  private void ResetLocked()
  {
    _fetcher.Reset();
    _decoder.Flush();
    _lastPaintedTs = -1;
  }

  private bool TickLocked(out MotionFrame? frame)
  {
    frame = null;
    if (_feed == null) return false;
    var view = _view;

    if (view.TimestampUs > 0)
    {
      if (!view.Paused)
      {
        var windowUs = (long)(WindowBaseUs * Math.Max(1, view.Rate));
        var from = view.Direction == 1 ? view.TimestampUs : view.TimestampUs + windowUs;
        var to = view.Direction == 1 ? view.TimestampUs + windowUs : view.TimestampUs - windowUs;
        _fetcher.SetTarget(from, to);
      }
      _decoder.SetTarget(GopPlanner.ComputeNeededGops(
        _fetcher.GopTimestamps(), view.TimestampUs, view.Rate, view.Direction));
    }

    var candidate = _decoder.GetFrame(view.TimestampUs);
    if (candidate == null || Math.Abs(view.TimestampUs - candidate.TimestampUs) > HoldLimitUs)
    {
      if (_lastPaintedTs < 0) return false;
      _lastPaintedTs = -1;
      return true;
    }
    if (candidate.TimestampUs == _lastPaintedTs) return false;
    _lastPaintedTs = candidate.TimestampUs;
    frame = candidate;
    return true;
  }

  private void HandleGop(GopMessage gop)
  {
    bool changed;
    MotionFrame? frame;
    lock (_lock)
    {
      _fetcher.AppendData(gop.Timestamp, gop.Data, (gop.Flags & GopFlags.Begin) != 0);
      changed = TickLocked(out frame);
    }
    if (changed) FrameChanged?.Invoke(frame);
  }

  private void HandleStatus(StreamStatus status)
  {
    lock (_lock)
    {
      switch (status)
      {
        case StreamStatus.FetchComplete:
          _fetcher.HandleFetchComplete();
          break;
        case StreamStatus.Live:
          _fetcher.HandleLive();
          break;
        case StreamStatus.Recording:
          _fetcher.HandleRecording();
          break;
        case StreamStatus.Error:
          _logger.LogWarning("Motion overlay stream error");
          break;
      }
    }
  }

  private void HandleGap(GapStatus gap)
  {
    lock (_lock) _fetcher.HandleGap(gap.From, gap.To);
  }
}
