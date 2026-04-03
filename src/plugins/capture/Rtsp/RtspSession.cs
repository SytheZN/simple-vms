using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Shared.Models;
using Shared.Models.Formats;

namespace Capture.Rtsp;

internal sealed class RtspSession : IAsyncDisposable
{
  private readonly string _uri;
  private readonly string? _username;
  private readonly string? _password;
  private readonly ILogger _logger;
  private readonly ConcurrentDictionary<int, Action<ReadOnlyMemory<byte>>> _channelHandlers = new();
  private readonly List<TrackRegistration> _tracks = [];
  private readonly SemaphoreSlim _demandLock = new(1, 1);

  private RtspClient? _client;
  private CancellationTokenSource? _readCts;
  private Task? _readLoop;
  private string? _lastSdp;
  private int _demandCount;
  private bool _disposed;

  public string Uri => _uri;

  public RtspSession(string uri, string? username, string? password, ILogger logger)
  {
    _uri = uri;
    _username = username;
    _password = password;
    _logger = logger;
  }

  public async Task<TrackRegistration> EnsureTrackAsync(string mediaType, CancellationToken ct)
  {
    var existing = _tracks.FirstOrDefault(t => t.MediaType == mediaType);
    if (existing != null)
      return existing;

    var client = new RtspClient();
    await client.ConnectAndDescribeAsync(_uri, _username, _password, ct);
    _lastSdp = client.LastSdp;

    var descriptions = SdpParser.Parse(_lastSdp!);
    var media = descriptions.FirstOrDefault(m => m.MediaType == mediaType)
      ?? throw new InvalidOperationException($"No {mediaType} track found in SDP");

    var rtpChannel = _tracks.Count * 2;
    var codecUpper = media.Codec.ToUpperInvariant();

    var (streamInfo, dataStream) = CreateTrackDataStream(codecUpper, media);

    var track = new TrackRegistration(mediaType, media.ControlUri, rtpChannel, codecUpper, streamInfo, dataStream);
    _tracks.Add(track);

    await client.SetupAsync(media.ControlUri, rtpChannel, ct);
    await client.PlayAsync(ct);

    _logger.LogDebug("Probed {MediaType} track: channels {Rtp}-{Rtcp}, codec {Codec}, control {Control}",
      mediaType, rtpChannel, rtpChannel + 1, codecUpper, media.ControlUri);

    await client.TeardownAsync(ct);
    await client.DisposeAsync();

    return track;
  }

  public async Task AddDemandAsync(CancellationToken ct)
  {
    await _demandLock.WaitAsync(ct);
    try
    {
      _demandCount++;
      if (_demandCount == 1)
        await ConnectAllAsync(ct);
    }
    finally
    {
      _demandLock.Release();
    }
  }

  public async Task RemoveDemandAsync()
  {
    await _demandLock.WaitAsync();
    try
    {
      _demandCount--;
      if (_demandCount <= 0)
      {
        _demandCount = 0;
        await DisconnectAsync();
      }
    }
    finally
    {
      _demandLock.Release();
    }
  }

  public Task Completed => _readLoop ?? Task.CompletedTask;

  private async Task ConnectAllAsync(CancellationToken ct)
  {
    await DisconnectAsync();

    _client = new RtspClient();
    await _client.ConnectAndDescribeAsync(_uri, _username, _password, ct);

    _channelHandlers.Clear();
    foreach (var track in _tracks)
    {
      var actualChannel = await _client.SetupAsync(track.ControlUri, track.RtpChannel, ct);
      if (actualChannel != track.RtpChannel)
      {
        _logger.LogDebug("Camera assigned channel {Actual} instead of requested {Requested} for {MediaType}",
          actualChannel, track.RtpChannel, track.MediaType);
      }
      RegisterTrackHandler(track, actualChannel);
      _logger.LogDebug("Set up {MediaType} on channel {Channel}: {Control}",
        track.MediaType, actualChannel, track.ControlUri);
    }

    await _client.PlayAsync(ct);
    _logger.LogDebug("RTSP session playing for {Uri} with {Count} track(s)", _uri, _tracks.Count);

    _readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    StartReadLoop(_readCts.Token);
  }

  private async Task DisconnectAsync()
  {
    if (_readCts != null)
    {
      _readCts.Cancel();
      if (_readLoop != null)
      {
        try { await _readLoop; } catch { }
      }
      _readCts.Dispose();
      _readCts = null;
      _readLoop = null;
    }

    if (_client != null)
    {
      try { await _client.TeardownAsync(CancellationToken.None); } catch { }
      await _client.DisposeAsync();
      _client = null;
    }
  }

  private void StartReadLoop(CancellationToken ct)
  {
    _readLoop = Task.Run(async () =>
    {
      var channelCounts = new ConcurrentDictionary<int, long>();
      try
      {
        while (!ct.IsCancellationRequested)
        {
          var frame = await _client!.ReadInterleavedFrameAsync(ct);
          if (frame == null)
          {
            break;
          }

          var (channel, payload) = frame.Value;
          var count = channelCounts.AddOrUpdate(channel, 1, (_, c) => c + 1);
          if (count == 1)
            _logger.LogDebug("First packet on channel {Channel} ({Bytes} bytes) for {Uri}",
              channel, payload.Length, _uri);

          if (_channelHandlers.TryGetValue(channel, out var handler))
            handler(payload);
        }
      }
      catch (OperationCanceledException) { }
      catch (IOException ex)
      {
        _logger.LogDebug("RTSP session IO error for {Uri}: {Message}", _uri, ex.Message);
      }

      var summary = string.Join(", ", channelCounts.OrderBy(kv => kv.Key)
        .Select(kv => $"ch{kv.Key}={kv.Value}"));
      _logger.LogDebug("RTSP session ended for {Uri} (packets: {Summary})", _uri, summary);
    }, ct);
  }

  private static (StreamInfo Info, IDataStream Stream) CreateTrackDataStream(
    string codecUpper, SdpMediaDescription media)
  {
    if (codecUpper == "H264")
    {
      var info = new StreamInfo { DataFormat = "h264", FormatParameters = RtspConnection.BuildH264Parameters(media) };
      return (info, new DataStream<H264NalUnit>(info));
    }
    if (codecUpper == "H265")
    {
      var info = new StreamInfo { DataFormat = "h265", FormatParameters = RtspConnection.BuildH265Parameters(media) };
      return (info, new DataStream<H265NalUnit>(info));
    }
    throw new InvalidOperationException($"Unsupported codec: {codecUpper}");
  }

  private void RegisterTrackHandler(TrackRegistration track, int actualChannel)
  {
    var codecUpper = track.Codec;

    if (codecUpper == "H264" && track.DataStream is DataStream<H264NalUnit> h264Stream)
    {
      var depacketizer = new RtpH264Depacketizer();
      _channelHandlers[actualChannel] = payload =>
      {
        if (payload.Length < 12) return;
        var rtpPayload = RtspConnection.ExtractRtpPayload(payload);
        var timestamp = RtspConnection.ExtractRtpTimestamp(payload);
        var nalType = rtpPayload.Span[0] & 0x1F;
        if (nalType == 24)
        {
          foreach (var unit in depacketizer.ProcessStapAAll(rtpPayload.Span, timestamp))
            h264Stream.Writer.TryWrite((H264NalUnit)unit);
        }
        else
        {
          var unit = depacketizer.ProcessPacket(rtpPayload.Span, timestamp);
          if (unit is H264NalUnit h264Unit)
            h264Stream.Writer.TryWrite(h264Unit);
        }
      };
    }
    else if (codecUpper == "H265" && track.DataStream is DataStream<H265NalUnit> h265Stream)
    {
      var depacketizer = new RtpH265Depacketizer();
      _channelHandlers[actualChannel] = payload =>
      {
        if (payload.Length < 12) return;
        var rtpPayload = RtspConnection.ExtractRtpPayload(payload);
        var timestamp = RtspConnection.ExtractRtpTimestamp(payload);
        var nalType = (rtpPayload.Span[0] >> 1) & 0x3F;
        if (nalType == 48)
        {
          foreach (var unit in depacketizer.ProcessApAll(rtpPayload.Span, timestamp))
            h265Stream.Writer.TryWrite((H265NalUnit)unit);
        }
        else
        {
          var unit = depacketizer.ProcessPacket(rtpPayload.Span, timestamp);
          if (unit is H265NalUnit h265Unit)
            h265Stream.Writer.TryWrite(h265Unit);
        }
      };
    }
  }

  public async ValueTask DisposeAsync()
  {
    if (_disposed) return;
    _disposed = true;
    await DisconnectAsync();
  }

  internal sealed record TrackRegistration(
    string MediaType, string ControlUri, int RtpChannel, string Codec,
    StreamInfo Info, IDataStream DataStream);
}
