using Microsoft.Extensions.Logging;
using SimpleVms.FFmpeg.Native;

namespace Client.Core.Decoding.Backends;

public sealed unsafe class SwDecodeBackend : IDecodeBackend
{
  private readonly ILogger _logger;
  private readonly FrameConverter _converter = new();
  private AVCodecContext* _ctx;
  private AVPacket* _pkt;
  private AVFrame* _frame;
  private bool _disposed;
  private int _sendLogCount;
  private int _captureLogCount;

  public FrameKind Kind => FrameKind.Cpu;
  public string DisplayName => "SW Decode";

  public SwDecodeBackend(ILogger<SwDecodeBackend> logger)
  {
    _logger = logger;
  }

  public bool Configure(CodecParameters config)
  {
    FfmpegLoader.EnsureLoaded();
    _logger.LogDebug("Configuring SW decoder: {Codec} {Width}x{Height} extradata={ExtradataLen}",
      config.Codec, config.Width, config.Height, config.Extradata.Length);
    Reset();

    var codecId = config.Codec switch
    {
      VideoCodec.H264 => AVCodecID.AV_CODEC_ID_H264,
      VideoCodec.H265 => AVCodecID.AV_CODEC_ID_HEVC,
      VideoCodec.Mjpeg => AVCodecID.AV_CODEC_ID_MJPEG,
      VideoCodec.MjpegB => AVCodecID.AV_CODEC_ID_MJPEGB,
      _ => AVCodecID.AV_CODEC_ID_NONE
    };

    if (codecId == AVCodecID.AV_CODEC_ID_NONE)
    {
      _logger.LogError("Unsupported codec: {Codec}", config.Codec);
      return false;
    }

    var codec = FFAvCodec.avcodec_find_decoder(codecId);
    if (codec == null)
    {
      _logger.LogError("FFmpeg decoder not found for {Codec}", config.Codec);
      return false;
    }

    _ctx = FFAvCodec.avcodec_alloc_context3(codec);
    if (_ctx == null)
    {
      _logger.LogError("Failed to allocate codec context");
      return false;
    }

    if (config.Extradata.Length > 0)
    {
      var size = config.Extradata.Length;
      _ctx->extradata = (byte*)FFAvUtil.av_mallocz((nuint)(size + 64));
      if (_ctx->extradata != null)
      {
        fixed (byte* src = config.Extradata)
          Buffer.MemoryCopy(src, _ctx->extradata, size + 64, size);
        _ctx->extradata_size = size;
      }
    }

    // Packet pts is in microseconds (our TimestampUs). FFmpeg needs a timebase
    // to propagate pts through the decoder; without it, output frames get
    // AV_NOPTS_VALUE.
    _ctx->pkt_timebase = new AVRational { num = 1, den = 1_000_000 };

    var ret = FFAvCodec.avcodec_open2(_ctx, codec, null);
    if (ret < 0)
    {
      _logger.LogError("Failed to open codec: {Error}", ret);
      Reset();
      return false;
    }

    _pkt = FFAvCodec.av_packet_alloc();
    _frame = FFAvUtil.av_frame_alloc();

    _logger.LogDebug("SW decoder opened for {Codec}", config.Codec);
    return true;
  }

  public bool SendSample(DemuxedSample sample)
  {
    if (_ctx == null || _pkt == null) return false;
    FFAvCodec.av_packet_unref(_pkt);

    fixed (byte* data = sample.Data.Span)
    {
      _pkt->data = data;
      _pkt->size = sample.Data.Length;
      // Bindings declare AVPacket pts/dts/duration as `nint` annotated int64_t:
      // a widening cast on 64-bit, but on 32-bit it silently truncates. Use
      // `checked` so a 32-bit build throws OverflowException instead of
      // producing corrupt timestamps.
      _pkt->pts = checked((nint)sample.TimestampUs);
      _pkt->dts = checked((nint)sample.DecodeTimestampUs);
      _pkt->duration = checked((nint)sample.DurationUs);

      if (_logger.IsEnabled(LogLevel.Trace) && _sendLogCount++ % 30 == 0)
        _logger.LogTrace("SendPacket pts={Pts} size={Size} key={IsKey}",
          (long)_pkt->pts, _pkt->size, sample.IsKey);

      var ret = FFAvCodec.avcodec_send_packet(_ctx, _pkt);
      _pkt->data = null;
      _pkt->size = 0;

      if (ret < 0)
        _logger.LogDebug("avcodec_send_packet returned {Error}", ret);

      return ret >= 0;
    }
  }

  public bool TryReceiveFrame(out DecodedFrame? frame)
  {
    frame = null;
    if (_ctx == null || _frame == null) return false;

    FFAvUtil.av_frame_unref(_frame);
    var ret = FFAvCodec.avcodec_receive_frame(_ctx, _frame);
    if (ret < 0) return false;

    frame = BuildCpuFrame(_frame);
    return frame != null;
  }

  private CpuDecodedFrame? BuildCpuFrame(AVFrame* src)
  {
    var w = src->width;
    var h = src->height;
    if (w == 0 || h == 0) return null;

    var stride = w * 4;
    var size = stride * h;
    var pixels = (nint)FFAvUtil.av_malloc((nuint)size);
    if (pixels == 0) return null;

    _converter.Convert(src, (byte*)pixels, stride);

    const long AV_NOPTS_VALUE = unchecked((long)0x8000000000000000);
    var timestampUs = (long)src->best_effort_timestamp;
    if (timestampUs == AV_NOPTS_VALUE)
      timestampUs = (long)src->pts;
    var durationUs = (long)src->duration;

    if (_logger.IsEnabled(LogLevel.Trace) && _captureLogCount++ % 10 == 0)
      _logger.LogTrace("CaptureFrame pts={Pts} best_effort={Best} duration={Duration}",
        (long)src->pts, (long)src->best_effort_timestamp, durationUs);

    // Skip AV_NOPTS_VALUE and zero: without a valid wall-clock the frame
    // can't be looked up by GetFrame anyway.
    if (timestampUs == AV_NOPTS_VALUE || timestampUs <= 0)
    {
      FFAvUtil.av_free((void*)pixels);
      return null;
    }

    return new CpuDecodedFrame(timestampUs, durationUs, pixels, w, h, stride);
  }

  public void Flush()
  {
    if (_ctx != null)
      FFAvCodec.avcodec_flush_buffers(_ctx);
  }

  public void Dispose()
  {
    if (_disposed) return;
    _disposed = true;
    Reset();
    _converter.Dispose();
  }

  private void Reset()
  {
    if (_frame != null)
    {
      var f = _frame;
      FFAvUtil.av_frame_free(&f);
      _frame = null;
    }
    if (_pkt != null)
    {
      var p = _pkt;
      FFAvCodec.av_packet_free(&p);
      _pkt = null;
    }
    if (_ctx != null)
    {
      var c = _ctx;
      FFAvCodec.avcodec_free_context(&c);
      _ctx = null;
    }
  }
}
