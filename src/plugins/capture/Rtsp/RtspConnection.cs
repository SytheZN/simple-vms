using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Models;
using Shared.Models.Formats;

namespace Capture.Rtsp;

public sealed class RtspConnection : IStreamConnection
{
  private readonly RtspClient _client;
  private readonly IRtpDepacketizer _depacketizer;
  private readonly IDataStream _dataStream;
  private readonly ILogger _logger;
  private readonly CancellationTokenSource _cts = new();
  private Task? _readLoop;

  public StreamInfo Info { get; }
  public IDataStream DataStream => _dataStream;
  public Task Completed => _readLoop ?? Task.CompletedTask;

  private RtspConnection(
    RtspClient client, IRtpDepacketizer depacketizer, IDataStream dataStream,
    StreamInfo info, ILogger logger)
  {
    _client = client;
    _depacketizer = depacketizer;
    _dataStream = dataStream;
    Info = info;
    _logger = logger;
  }

  public static async Task<RtspConnection> CreateAsync(
    string uri, IReadOnlyDictionary<string, string>? credentials,
    string mediaType = "video", ILogger? logger = null, CancellationToken ct = default)
  {
    var log = logger ?? NullLogger.Instance;
    var client = new RtspClient();
    var username = credentials?.GetValueOrDefault("username");
    var password = credentials?.GetValueOrDefault("password");
    var sdpText = await client.ConnectAndDescribeAsync(uri, username, password, ct);
    log.LogTrace("SDP for {Uri}:\n{Sdp}", uri, sdpText);
    var mediaDescriptions = SdpParser.Parse(sdpText);

    var media = mediaDescriptions.FirstOrDefault(m => m.MediaType == mediaType)
      ?? throw new InvalidOperationException($"No {mediaType} track found in SDP");
    log.LogDebug("Selected {MediaType} track: codec={Codec} control={Control}",
      mediaType, media.Codec, media.ControlUri);
    await client.SetupAsync(media.ControlUri, ct);
    await client.PlayAsync(ct);

    IRtpDepacketizer depacketizer;
    IDataStream dataStream;
    StreamInfo info;

    var codecUpper = media.Codec.ToUpperInvariant();

    if (codecUpper == "H264")
    {
      depacketizer = new RtpH264Depacketizer();
      var parameters = BuildH264Parameters(media);
      info = new StreamInfo
      {
        DataFormat = "h264",
        FormatParameters = parameters
      };
      var stream = new DataStream<H264NalUnit>(info);
      dataStream = stream;

      var connection = new RtspConnection(client, depacketizer, dataStream, info, log);
      connection.StartReadLoop(stream);
      return connection;
    }
    else if (codecUpper == "H265")
    {
      depacketizer = new RtpH265Depacketizer();
      var parameters = BuildH265Parameters(media);
      info = new StreamInfo
      {
        DataFormat = "h265",
        FormatParameters = parameters
      };
      var stream = new DataStream<H265NalUnit>(info);
      dataStream = stream;

      var connection = new RtspConnection(client, depacketizer, dataStream, info, log);
      connection.StartReadLoop(stream);
      return connection;
    }
    else
    {
      await client.DisposeAsync();
      throw new InvalidOperationException($"Unsupported codec: {media.Codec}");
    }
  }

  private void StartReadLoop<T>(DataStream<T> stream) where T : IDataUnit
  {
    _readLoop = Task.Run(async () =>
    {
      try
      {
        while (!_cts.Token.IsCancellationRequested)
        {
          var frame = await _client.ReadInterleavedFrameAsync(_cts.Token);
          if (frame == null)
            break;

          var (channel, payload) = frame.Value;
          if (channel != 0)
            continue;

          if (payload.Length < 12)
            continue;

          var rtpPayload = ExtractRtpPayload(payload);
          var timestamp = ExtractRtpTimestamp(payload);

          if (_depacketizer is RtpH264Depacketizer h264 && stream is DataStream<H264NalUnit> h264Stream)
          {
            var nalType = rtpPayload.Span[0] & 0x1F;
            if (nalType == 24) // STAP-A
            {
              foreach (var unit in h264.ProcessStapAAll(rtpPayload.Span, timestamp))
                await h264Stream.Writer.WriteAsync((H264NalUnit)unit, _cts.Token);
            }
            else
            {
              var unit = h264.ProcessPacket(rtpPayload.Span, timestamp);
              if (unit is H264NalUnit h264Unit)
                await h264Stream.Writer.WriteAsync(h264Unit, _cts.Token);
            }
          }
          else if (_depacketizer is RtpH265Depacketizer h265 && stream is DataStream<H265NalUnit> h265Stream)
          {
            var nalType = (rtpPayload.Span[0] >> 1) & 0x3F;
            if (nalType == 48) // AP
            {
              foreach (var unit in h265.ProcessApAll(rtpPayload.Span, timestamp))
                await h265Stream.Writer.WriteAsync((H265NalUnit)unit, _cts.Token);
            }
            else
            {
              var unit = h265.ProcessPacket(rtpPayload.Span, timestamp);
              if (unit is H265NalUnit h265Unit)
                await h265Stream.Writer.WriteAsync(h265Unit, _cts.Token);
            }
          }
        }
      }
      catch (OperationCanceledException) { }
      catch (IOException) { }
      finally
      {
        stream.Complete();
      }
    });
  }

  internal static ReadOnlyMemory<byte> ExtractRtpPayload(ReadOnlyMemory<byte> rtpPacket)
  {
    var span = rtpPacket.Span;
    if (span.Length < 12)
      return ReadOnlyMemory<byte>.Empty;

    var cc = span[0] & 0x0F;
    var hasExtension = (span[0] & 0x10) != 0;
    var offset = 12 + cc * 4;

    if (hasExtension && offset + 4 <= span.Length)
    {
      var extensionLength = (span[offset + 2] << 8) | span[offset + 3];
      offset += 4 + extensionLength * 4;
    }

    return offset < rtpPacket.Length ? rtpPacket[offset..] : ReadOnlyMemory<byte>.Empty;
  }

  internal static ulong ExtractRtpTimestamp(ReadOnlyMemory<byte> rtpPacket)
  {
    var span = rtpPacket.Span;
    if (span.Length < 8)
      return 0;

    return ((ulong)span[4] << 24) | ((ulong)span[5] << 16) |
           ((ulong)span[6] << 8) | span[7];
  }

  internal static H264Parameters? BuildH264Parameters(SdpMediaDescription media)
  {
    if (!media.FormatParameters.TryGetValue("sprop-parameter-sets", out var spropSets))
      return null;

    var parts = spropSets.Split(',', StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length < 2)
      return null;

    return new H264Parameters
    {
      Sps = Convert.FromBase64String(parts[0]),
      Pps = Convert.FromBase64String(parts[1])
    };
  }

  internal static H265Parameters? BuildH265Parameters(SdpMediaDescription media)
  {
    var hasVps = media.FormatParameters.TryGetValue("sprop-vps", out var vpsB64);
    var hasSps = media.FormatParameters.TryGetValue("sprop-sps", out var spsB64);
    var hasPps = media.FormatParameters.TryGetValue("sprop-pps", out var ppsB64);

    if (!hasVps || !hasSps || !hasPps)
      return null;

    return new H265Parameters
    {
      Vps = Convert.FromBase64String(vpsB64!),
      Sps = Convert.FromBase64String(spsB64!),
      Pps = Convert.FromBase64String(ppsB64!)
    };
  }

  public async ValueTask DisposeAsync()
  {
    _cts.Cancel();
    if (_readLoop != null)
    {
      try { await _readLoop; }
      catch { /* swallow */ }
    }
    await _client.DisposeAsync();
    _cts.Dispose();
  }
}
