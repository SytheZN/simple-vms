using System.Diagnostics;
using Avalonia.Threading;
using Client.Core.Decoding.Diagnostics;
using Client.Core.Streaming;
using Microsoft.Extensions.Logging;
using Shared.Protocol;

namespace Client.Core.Decoding;

public sealed class Player : IDisposable
{
  public enum PlayerState { Idle, Seeking, Streaming }
  public enum PlayerMode { Live, Playback }

  private readonly ILogger _logger;
  private readonly Fetcher _fetcher = new();
  private readonly Decoder _decoder;
  private readonly IFrameRenderer _renderer;
  private readonly ILiveStreamService _liveService;
  private readonly IPlaybackService _playbackService;

  private Guid _cameraId;
  private string _currentProfile = "main";

  private IVideoFeed? _feed;
  private CodecParameters? _codecConfig;
  private uint _timescale = 90000;

  private PlayerState _state = PlayerState.Idle;
  private PlayerMode _mode = PlayerMode.Live;
  private bool _ignoreData;

  private long _playheadUs;
  private double _rate = 1.0;
  private int _direction = 1;
  private int _stride = 1;
  private bool _paused;
  private double _minRate = 1;
  private double _maxRate = 1;

  private long _lastTickTimestamp;
  private double _accumUs;
  private long _lastFrameDurationUs = 40_000;
  private bool _suppressBuffering;
  private bool _buffering;

  private long _seekTargetUs;
  private long _seekRenderTargetUs;
  private readonly List<(ulong Ts, ReadOnlyMemory<byte> Data)> _seekBuffer = [];
  private ulong _seekGapEnd;
  private const double LiveCatchupMaxBoost = 0.1;
  private const double LiveCatchupTauUs = 500_000;
  private const long LiveCatchupUpperUs = 500_000;
  private const long LiveCatchupLowerUs = 200_000;

  private long _scrubPendingTs;
  private bool _scrubRunning;

  private long _lastPublishedPositionUs;
  private double _lastCatchup = 1.0;
  private long _lastBufferUs;
  public event Action<long>? CurrentPositionChanged;
  public event Action<bool>? BufferingChanged;
  public event Action<PlayerMode>? ModeChanged;
  public event Action<double>? RateChanged;
  public event Action<bool>? PausedChanged;

  private bool _disposed;

  public Player(ILoggerFactory loggerFactory, IDecodeBackend backend, IFrameRenderer renderer,
    ILiveStreamService liveService, IPlaybackService playbackService,
    DiagnosticsSettings diagnosticsSettings)
  {
    _logger = loggerFactory.CreateLogger<Player>();
    _renderer = renderer;
    _decoder = new Decoder(loggerFactory.CreateLogger<Decoder>(), backend, _fetcher);
    _liveService = liveService;
    _playbackService = playbackService;
    _liveService.FeedReplaced += OnFeedReplaced;
    Diagnostics = new PlaybackDiagnostics(BuildStats, diagnosticsSettings);
  }

  public PlaybackDiagnostics Diagnostics { get; }
  public IFrameRenderer Renderer => _renderer;

  private PlaybackStats BuildStats() => new(
    _decoder.BackendDisplayName, _renderer.DisplayName,
    _state.ToString(), _mode.ToString(),
    _rate, _lastCatchup,
    Interlocked.Read(ref _playheadUs), _lastBufferUs,
    _fetcher.BufferedGopCount, _fetcher.BufferedBytes,
    _decoder.CachedGopCount, _decoder.CachedFrameCount,
    _buffering);
  public long CurrentPositionUs => Interlocked.Read(ref _playheadUs);
  public double Rate => _rate;
  public int Direction => _direction;
  public int Stride => _stride;
  public bool Paused => _paused;
  public bool Buffering => _buffering;
  public PlayerMode Mode => _mode;
  public double MinRate => _minRate;
  public double MaxRate => _maxRate;
  public string CurrentProfile => _currentProfile;

  public void Configure(Guid cameraId, string profile)
  {
    _cameraId = cameraId;
    _currentProfile = profile;
  }

  public async Task DetachAsync()
  {
    await UnsubscribeCurrentAsync();
    Stop();
  }

  public void Stop()
  {
    _state = PlayerState.Idle;
    _fetcher.Reset();
    _seekBuffer.Clear();
    _seekTargetUs = 0;
    _seekRenderTargetUs = 0;
    Interlocked.Exchange(ref _playheadUs, 0);
    _rate = 1;
    _direction = 1;
    var wasPaused = _paused;
    _paused = false;
    _buffering = false;
    _ignoreData = false;
    _codecConfig = null;
    _renderer.Clear();
    if (wasPaused) PausedChanged?.Invoke(false);
  }

  public void Dispose()
  {
    if (_disposed) return;
    _disposed = true;
    _liveService.FeedReplaced -= OnFeedReplaced;
    DetachAsync().AsSyncWait(TimeSpan.FromSeconds(1));
    _decoder.Dispose();
    _renderer.Dispose();
  }

  private void EnterSeeking(long ts)
  {
    _logger.LogDebug("enterSeeking ts={Ts} mode={Mode}", ts, _mode);
    _state = PlayerState.Seeking;
    _seekBuffer.Clear();
    _seekGapEnd = 0;
    _seekTargetUs = ts;
    _seekRenderTargetUs = 0;
    _ignoreData = true;
    _buffering = false;
    BufferingChanged?.Invoke(false);
    _fetcher.Reset();
    _decoder.FlushForSeek();
  }

  private void EnterStreaming()
  {
    _logger.LogDebug("enterStreaming");
    _state = PlayerState.Streaming;
    _lastTickTimestamp = Stopwatch.GetTimestamp();
    _accumUs = 0;
  }

  private void CommitLive()
  {
    long newestWallClock = 0;
    var newestIdx = -1;
    for (var i = 0; i < _seekBuffer.Count; i++)
    {
      var samples = Fmp4Demuxer.DemuxGop(_seekBuffer[i].Data.Span, _timescale);
      foreach (var s in samples)
      {
        if (s.TimestampUs > newestWallClock)
        {
          newestWallClock = s.TimestampUs;
          newestIdx = i;
        }
      }
    }

    if (newestWallClock == 0) return;

    for (var i = newestIdx; i < _seekBuffer.Count; i++)
      _fetcher.AppendData(_seekBuffer[i].Ts, _seekBuffer[i].Data);
    var dropped = newestIdx;
    _seekBuffer.Clear();
    _seekRenderTargetUs = newestWallClock;
    _logger.LogDebug("commitLive anchor={Ts} droppedStale={Dropped}",
      newestWallClock, dropped);
  }

  private void CommitSeek()
  {
    _logger.LogDebug("commitSeek chunks={Count} target={Target}", _seekBuffer.Count, _seekTargetUs);
    for (var i = 0; i < _seekBuffer.Count; i++)
      _fetcher.AppendData(_seekBuffer[i].Ts, _seekBuffer[i].Data);
    _seekBuffer.Clear();
    _seekRenderTargetUs = _seekTargetUs;
  }

  private async Task UnsubscribeCurrentAsync()
  {
    var feed = _feed;
    if (feed == null) return;
    feed.OnInit -= HandleInit;
    feed.OnGop -= HandleGopMsg;
    feed.OnStatus -= HandleStatus;
    feed.OnGap -= HandleGapMsg;
    _feed = null;
    _fetcher.Detach();

    if (_mode == PlayerMode.Live)
      await _liveService.UnsubscribeAsync(feed, CancellationToken.None);
    else
      await _playbackService.StopAsync(feed, CancellationToken.None);
  }

  private void AttachFeed(IVideoFeed feed)
  {
    _feed = feed;
    feed.OnInit += HandleInit;
    feed.OnGop += HandleGopMsg;
    feed.OnStatus += HandleStatus;
    feed.OnGap += HandleGapMsg;
    _fetcher.Attach((from, to) => feed.SendFetchAsync(from, to, CancellationToken.None));
    if (!feed.LastInit.IsEmpty)
      HandleInit(feed.LastInit);
  }

  private void OnFeedReplaced(IVideoFeed oldFeed, IVideoFeed newFeed)
  {
    if (!ReferenceEquals(oldFeed, _feed)) return;
    _logger.LogDebug("FeedReplaced: swapping to new live feed");
    oldFeed.OnInit -= HandleInit;
    oldFeed.OnGop -= HandleGopMsg;
    oldFeed.OnStatus -= HandleStatus;
    oldFeed.OnGap -= HandleGapMsg;
    _feed = null;
    _fetcher.Detach();
    EnterSeeking(0);
    AttachFeed(newFeed);
  }

  public async Task GoLiveAsync(CancellationToken ct)
  {
    _logger.LogDebug("GoLive state={State}", _state);
    await UnsubscribeCurrentAsync();
    _mode = PlayerMode.Live;
    EnterSeeking(0);
    _paused = false;
    var feed = await _liveService.SubscribeAsync(_cameraId, _currentProfile, ct);
    AttachFeed(feed);
  }

  public async Task SeekAsync(long ts, CancellationToken ct)
  {
    _logger.LogDebug("Seek ts={Ts} state={State}", ts, _state);
    await UnsubscribeCurrentAsync();
    _mode = PlayerMode.Playback;
    EnterSeeking(ts);
    var feed = await _playbackService.StartAsync(_cameraId, _currentProfile, (ulong)ts, null, ct);
    AttachFeed(feed);
  }

  public async Task SetProfileAsync(string profile, CancellationToken ct)
  {
    _currentProfile = profile;
    if (_mode == PlayerMode.Live)
      await GoLiveAsync(ct);
    else
      await SeekAsync(Interlocked.Read(ref _playheadUs), ct);
  }

  public void TogglePause()
  {
    _paused = !_paused;
    if (!_paused)
    {
      _lastTickTimestamp = Stopwatch.GetTimestamp();
      _accumUs = 0;
    }
    PausedChanged?.Invoke(_paused);
  }

  public void SetRate(double r)
  {
    if (_mode == PlayerMode.Live) return;
    var newDir = r < 0 ? -1 : 1;
    var newRate = Math.Abs(r);
    var dirChanged = newDir != _direction;
    var rateChanged = Math.Abs(newRate - _rate) > 1e-9;

    _direction = newDir;
    _rate = newRate;
    if (rateChanged) RateChanged?.Invoke(r);

    if (dirChanged || rateChanged)
    {
      var newStride = newRate >= 3 ? (int)Math.Floor(newRate) : 1;
      _accumUs = 0;
      if (newStride != _stride)
      {
        _stride = newStride;
        _suppressBuffering = true;
        _decoder.SetStride(newStride);
      }
    }
  }

  public void ScrubStart()
  {
    _paused = true;
  }

  public void ScrubMove(long ts)
  {
    Interlocked.Exchange(ref _scrubPendingTs, ts);
    if (!_scrubRunning) _ = ScrubLoopAsync();
  }

  public async Task ScrubEndAsync(long ts, CancellationToken ct)
  {
    Interlocked.Exchange(ref _scrubPendingTs, 0);
    _paused = false;
    await SeekAsync(ts, ct);
  }

  private async Task ScrubLoopAsync()
  {
    _scrubRunning = true;
    while (Interlocked.Read(ref _scrubPendingTs) > 0 && _paused)
    {
      var ts = Interlocked.Exchange(ref _scrubPendingTs, 0);

      if (await ScrubRenderAsync(ts)) continue;

      await _fetcher.FetchAtAsync((ulong)ts);
      await ScrubRenderAsync(ts);
    }
    _scrubRunning = false;
  }

  private async Task<bool> ScrubRenderAsync(long ts)
  {
    var frame = _decoder.GetFrame(ts);
    if (frame != null && Math.Abs(frame.TimestampUs - ts) < 5_000_000)
    {
      PublishFrame(frame);
      return true;
    }

    var gop = _fetcher.FindGop((ulong)ts);
    if (gop != null)
    {
      var merged = Fetcher.MergedData(gop);
      await _decoder.DecodeKeyframeAsync(merged, gop.Timestamp);
      var kf = _decoder.GetFrame(ts);
      if (kf != null)
      {
        PublishFrame(kf);
        return true;
      }
    }
    return false;
  }

  private void HandleAck() => _ignoreData = false;

  private void HandleInit(ReadOnlyMemory<byte> data)
  {
    if (_ignoreData) return;
    var newConfig = Fmp4Demuxer.ParseInitSegment(data.Span);
    var newTimescale = Fmp4Demuxer.ParseTimescale(data.Span);
    if (newConfig == null) return;

    _timescale = newTimescale;
    _decoder.SetTimescale(newTimescale);

    if (_codecConfig == null
        || _codecConfig.Codec != newConfig.Codec
        || _codecConfig.Width != newConfig.Width
        || _codecConfig.Height != newConfig.Height)
    {
      _codecConfig = newConfig;
      _decoder.Configure(newConfig);
      _logger.LogDebug("Init parsed: {Codec} {Width}x{Height} timescale={Timescale}",
        newConfig.Codec, newConfig.Width, newConfig.Height, newTimescale);
    }
  }

  private void HandleGopMsg(GopMessage gop)
  {
    if (_ignoreData) return;

    switch (_state)
    {
      case PlayerState.Idle:
        break;

      case PlayerState.Seeking:
        if (_seekRenderTargetUs > 0)
        {
          _fetcher.AppendData(gop.Timestamp, gop.Data);
        }
        else
        {
          _seekBuffer.Add((gop.Timestamp, gop.Data));
          if (_mode == PlayerMode.Live)
            CommitLive();
          else if (_seekBuffer.Count == 1)
            CommitSeek();
        }
        break;

      case PlayerState.Streaming:
        _fetcher.AppendData(gop.Timestamp, gop.Data);
        break;
    }
  }

  private void HandleStatus(StreamStatus status)
  {
    switch (status)
    {
      case StreamStatus.Ack:
        HandleAck();
        break;
      case StreamStatus.FetchComplete:
        HandleFetchComplete();
        break;
      case StreamStatus.Live:
        _mode = PlayerMode.Live;
        _minRate = 1;
        _maxRate = 1;
        _rate = 1;
        _direction = 1;
        _fetcher.HandleLive();
        ModeChanged?.Invoke(PlayerMode.Live);
        break;
      case StreamStatus.Recording:
        _mode = PlayerMode.Playback;
        _minRate = -8;
        _maxRate = 8;
        _fetcher.HandleRecording();
        ModeChanged?.Invoke(PlayerMode.Playback);
        break;
    }
  }

  private void HandleFetchComplete()
  {
    _fetcher.HandleFetchComplete();
    if (_state == PlayerState.Seeking && _seekGapEnd > 0)
    {
      var from = (long)_seekGapEnd;
      _seekGapEnd = 0;
      _fetcher.SetTarget(from, from + 30_000_000);
    }
  }

  private void HandleGapMsg(GapStatus gap)
  {
    _fetcher.HandleGap(gap.From, gap.To);
    switch (_state)
    {
      case PlayerState.Idle:
        break;
      case PlayerState.Seeking:
        _seekGapEnd = gap.To;
        break;
      case PlayerState.Streaming:
        _decoder.ResetWallClock();
        var playhead = Interlocked.Read(ref _playheadUs);
        if (_direction == 1 && playhead < (long)gap.To) RenderAt((long)gap.To);
        else if (_direction == -1 && playhead > (long)gap.From) RenderAt((long)gap.From);
        break;
    }
  }

  private int _tickDebugCount;

  public void Tick()
  {
    if (_state == PlayerState.Idle) return;

    if (_state == PlayerState.Seeking)
    {
      if (_seekRenderTargetUs > 0)
      {
        var gopCount = _fetcher.GopTimestamps().Length;
        if (_tickDebugCount++ % 50 == 0)
          _logger.LogDebug("Seeking tick: seekRenderTarget={Target} gopsInCache={Gops}",
            _seekRenderTargetUs, gopCount);

        if (RenderAt(_seekRenderTargetUs))
        {
          _seekRenderTargetUs = 0;
          EnterStreaming();
        }
      }
      else if (_seekTargetUs > 0)
      {
        UpdatePipeline(_seekTargetUs);
      }
      return;
    }

    if (_paused) return;

    var now = Stopwatch.GetTimestamp();
    var elapsedMs = Stopwatch.GetElapsedTime(_lastTickTimestamp, now).TotalMilliseconds;
    _lastTickTimestamp = now;

    var effectiveDurationUs = (double)_lastFrameDurationUs * _stride;
    _lastCatchup = LiveCatchupMultiplier();
    _accumUs += elapsedMs * 1000.0 * _rate * _lastCatchup;

    if (_accumUs < effectiveDurationUs) return;

    var steps = (long)(_accumUs / effectiveDurationUs);
    _accumUs -= steps * effectiveDurationUs;
    var currentTs = Interlocked.Read(ref _playheadUs);
    var nextTs = currentTs + (long)(steps * effectiveDurationUs * _direction);

    if (!RenderAt(nextTs))
    {
      if (!_suppressBuffering)
      {
        _buffering = true;
        BufferingChanged?.Invoke(true);
      }
      _accumUs = 0;
      _lastTickTimestamp = Stopwatch.GetTimestamp();
      return;
    }

    _buffering = false;
    _suppressBuffering = false;
    BufferingChanged?.Invoke(false);
  }

  private double LiveCatchupMultiplier()
  {
    if (_mode != PlayerMode.Live) { _lastBufferUs = 0; return 1.0; }
    var newest = _decoder.NewestFrameTimestampUs;
    if (newest <= 0) { _lastBufferUs = 0; return 1.0; }
    var buffer = newest - Interlocked.Read(ref _playheadUs);
    _lastBufferUs = buffer;

    if (buffer > LiveCatchupUpperUs)
    {
      var excess = buffer - LiveCatchupUpperUs;
      return 1.0 + LiveCatchupMaxBoost * (1.0 - Math.Exp(-excess / LiveCatchupTauUs));
    }
    if (buffer < LiveCatchupLowerUs)
    {
      var deficit = LiveCatchupLowerUs - buffer;
      return 1.0 - LiveCatchupMaxBoost * (1.0 - Math.Exp(-deficit / LiveCatchupTauUs));
    }
    return 1.0;
  }

  private bool RenderAt(long ts)
  {
    UpdatePipeline(ts);
    var frame = _decoder.GetFrame(ts);
    if (frame == null)
    {
      if (_tickDebugCount % 50 == 0)
        _logger.LogDebug("RenderAt({Ts}): no frame available", ts);
      return false;
    }
    PublishFrame(frame);
    return true;
  }

  private void PublishFrame(DecodedFrame frame)
  {
    _renderer.RenderFrame(frame);
    if (Diagnostics.Enabled) Diagnostics.RecordFrame();

    Interlocked.Exchange(ref _playheadUs, frame.TimestampUs);
    if (frame.DurationUs > 0)
      _lastFrameDurationUs = frame.DurationUs;

    var pos = frame.TimestampUs;
    if (Math.Abs(pos - _lastPublishedPositionUs) > 16_000)
    {
      _lastPublishedPositionUs = pos;
      Dispatcher.UIThread.Post(() => CurrentPositionChanged?.Invoke(pos));
    }
  }

  private void UpdatePipeline(long ts)
  {
    var windowUs = 30_000_000L * Math.Max(1, (long)Math.Abs(_rate));
    var fromUs = _direction == 1 ? ts : ts + windowUs;
    var toUs = _direction == 1 ? ts + windowUs : ts - windowUs;
    _fetcher.SetTarget(fromUs, toUs);
    _decoder.SetTarget(ComputeNeededGops(ts));
  }

  private const long PrefetchBoundaryUs = 500_000;

  private ulong[] ComputeNeededGops(long ts)
  {
    var available = _fetcher.GopTimestamps();
    var currentIdx = FindGopIndex(available, ts);
    if (currentIdx < 0)
    {
      if (_tickDebugCount % 50 == 0 && available.Length > 0)
        _logger.LogDebug("ComputeNeededGops({Ts}): no matching gop, oldest={Oldest} newest={Newest}",
          ts, available[0], available[^1]);
      return [];
    }

    var needed = new List<ulong> { available[currentIdx] };

    var aheadIdx = currentIdx + _direction;
    if (aheadIdx >= 0 && aheadIdx < available.Length)
    {
      var boundaryUs = _direction == 1
        ? (long)available[aheadIdx]
        : (long)available[currentIdx];
      var distance = _direction == 1 ? boundaryUs - ts : ts - boundaryUs;
      if (distance < PrefetchBoundaryUs) needed.Add(available[aheadIdx]);
    }

    return [.. needed];
  }

  private static int FindGopIndex(ulong[] timestamps, long ts)
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

internal static class SyncWaitExtensions
{
  public static void AsSyncWait(this Task task, TimeSpan timeout) =>
    task.Wait(timeout);
}
