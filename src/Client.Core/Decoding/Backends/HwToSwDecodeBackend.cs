using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using SimpleVms.FFmpeg.Native;

namespace Client.Core.Decoding.Backends;

public sealed unsafe class HwToSwDecodeBackend : IDecodeBackend
{
  public static readonly AVHWDeviceType[] FullHwPriority =
  [
    AVHWDeviceType.AV_HWDEVICE_TYPE_VULKAN,
    AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI,
    AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA,
    AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2,
    AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX,
    AVHWDeviceType.AV_HWDEVICE_TYPE_MEDIACODEC,
  ];

  public static readonly AVHWDeviceType[] VulkanOnly =
    [AVHWDeviceType.AV_HWDEVICE_TYPE_VULKAN];

  public static readonly AVHWDeviceType[] PlatformOnly =
  [
    AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI,
    AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA,
    AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2,
    AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX,
    AVHWDeviceType.AV_HWDEVICE_TYPE_MEDIACODEC,
  ];

  private readonly ILogger _logger;
  private readonly AVHWDeviceType[] _hwPriority;
  private readonly bool _strictHw;
  private readonly string _strictLabel;
  private readonly FrameConverter _converter = new();
  private AVCodecContext* _ctx;
  private AVPacket* _pkt;
  private AVFrame* _frame;
  private AVFrame* _swFrame;
  private AVBufferRef* _hwDeviceCtx;
  private AVPixelFormat _hwPixelFormat = AVPixelFormat.AV_PIX_FMT_NONE;
  private bool _disposed;

  private delegate* unmanaged[Cdecl]<AVCodecContext*, AVPixelFormat*, AVPixelFormat> _getFormatCallback;
  private static AVPixelFormat _activeHwPixelFormat = AVPixelFormat.AV_PIX_FMT_NONE;
  private int _sendLogCount;
  private int _captureLogCount;

  public FrameKind Kind => FrameKind.Cpu;

  public string DisplayName => _hwPixelFormat switch
  {
    AVPixelFormat.AV_PIX_FMT_VULKAN => "HW Vulkan Decode",
    AVPixelFormat.AV_PIX_FMT_VAAPI => "HW VAAPI Decode",
    AVPixelFormat.AV_PIX_FMT_D3D11 => "HW D3D11 Decode",
    AVPixelFormat.AV_PIX_FMT_DXVA2_VLD => "HW DXVA2 Decode",
    AVPixelFormat.AV_PIX_FMT_VIDEOTOOLBOX => "HW VideoToolbox Decode",
    AVPixelFormat.AV_PIX_FMT_MEDIACODEC => "HW MediaCodec Decode",
    _ => _strictHw ? _strictLabel : (_ctx != null ? "SW Decode" : "HW Decode")
  };

  public HwToSwDecodeBackend(ILogger<HwToSwDecodeBackend> logger)
    : this(logger, FullHwPriority, strictHw: false, strictLabel: "HW Decode") { }

  public HwToSwDecodeBackend(
    ILogger<HwToSwDecodeBackend> logger,
    AVHWDeviceType[] hwPriority,
    bool strictHw,
    string strictLabel)
  {
    _logger = logger;
    _hwPriority = hwPriority;
    _strictHw = strictHw;
    _strictLabel = strictLabel;
  }

  public bool Configure(CodecParameters config)
  {
    FfmpegLoader.EnsureLoaded();
    _logger.LogDebug("Configuring decoder: {Codec} {Width}x{Height} extradata={ExtradataLen}",
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

    TryInitHardwareAccel(codec);
    if (_strictHw && _hwDeviceCtx == null)
    {
      _logger.LogError("Strict HW required but no allowed device available: {Allowed}",
        string.Join(",", _hwPriority));
      Reset();
      return false;
    }

    var ret = FFAvCodec.avcodec_open2(_ctx, codec, null);
    if (ret < 0)
    {
      _logger.LogError("Failed to open codec: {Error}", ret);
      Reset();
      return false;
    }

    _pkt = FFAvCodec.av_packet_alloc();
    _frame = FFAvUtil.av_frame_alloc();

    _logger.LogDebug("Decoder opened successfully for {Codec}", config.Codec);
    return true;
  }

  public bool SendSample(DemuxedSample sample)
  {
    if (_ctx == null || _pkt == null)
      return false;

    FFAvCodec.av_packet_unref(_pkt);

    fixed (byte* data = sample.Data.Span)
    {
      _pkt->data = data;
      _pkt->size = sample.Data.Length;
      // Bindings declare AVPacket pts/dts/duration as `nint` annotated int64_t:
      // a widening cast on 64-bit, but on 32-bit it silently truncates. Use
      // `checked` so a 32-bit build throws OverflowException instead of
      // producing corrupt timestamps. See native/ffmpeg/generate-bindings.sh.
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
    if (_ctx == null || _frame == null)
      return false;

    FFAvUtil.av_frame_unref(_frame);
    var ret = FFAvCodec.avcodec_receive_frame(_ctx, _frame);
    if (ret < 0)
      return false;

    var src = _frame;

    if (IsHardwareFormat((AVPixelFormat)src->format))
    {
      if (_swFrame == null)
        _swFrame = FFAvUtil.av_frame_alloc();
      FFAvUtil.av_frame_unref(_swFrame);
      if (FFAvUtil.av_hwframe_transfer_data(_swFrame, src, 0) < 0)
        return false;
      // av_hwframe_transfer_data copies pixel data only; pts/best_effort_timestamp/
      // duration stay at AV_NOPTS_VALUE/0 on the fresh sw frame unless we copy them.
      if (FFAvUtil.av_frame_copy_props(_swFrame, src) < 0)
        return false;
      src = _swFrame;
    }

    frame = BuildCpuFrame(src);
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
    // best_effort_timestamp is the decoder's reordered/recovered pts; for HW
    // decode paths where pts isn't set on output frames this is where the
    // timestamp survives.
    var timestampUs = (long)src->best_effort_timestamp;
    if (timestampUs == AV_NOPTS_VALUE)
      timestampUs = (long)src->pts;
    var durationUs = (long)src->duration;

    if (_logger.IsEnabled(LogLevel.Trace) && _captureLogCount++ % 10 == 0)
      _logger.LogTrace("CaptureFrame pts={Pts} best_effort={Best} duration={Duration}",
        (long)src->pts, (long)src->best_effort_timestamp, durationUs);

    // Skip AV_NOPTS_VALUE and zero: without a valid wall-clock the frame
    // can't be looked up by GetFrame anyway (and a literal zero inside a
    // stream whose timestamps are unix-micros is itself a sentinel for
    // "missing").
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

  private void TryInitHardwareAccel(AVCodec* codec)
  {
    foreach (var deviceType in _hwPriority)
    {
      if (!CodecSupportsDevice(codec, deviceType, out var hwPixFmt))
        continue;

      AVBufferRef* deviceCtx = null;
      if (FFAvUtil.av_hwdevice_ctx_create(&deviceCtx, deviceType, null, null, 0) < 0)
        continue;

      _hwDeviceCtx = deviceCtx;
      _hwPixelFormat = hwPixFmt;
      _activeHwPixelFormat = hwPixFmt;
      _ctx->hw_device_ctx = FFAvUtil.av_buffer_ref(_hwDeviceCtx);
      _getFormatCallback = &GetFormatCallback;
      _ctx->get_format = _getFormatCallback;
      _logger.LogDebug("HW acceleration enabled: {DeviceType} pixFmt={PixFmt}", deviceType, hwPixFmt);
      return;
    }

    _logger.LogDebug("No HW acceleration available, using software decode");
  }

  private static bool CodecSupportsDevice(AVCodec* codec, AVHWDeviceType deviceType, out AVPixelFormat hwPixFmt)
  {
    hwPixFmt = AVPixelFormat.AV_PIX_FMT_NONE;
    for (var i = 0; ; i++)
    {
      var hwConfig = FFAvCodec.avcodec_get_hw_config(codec, i);
      if (hwConfig == null) break;
      if (hwConfig->device_type == deviceType &&
          (hwConfig->methods & 2) != 0)
      {
        hwPixFmt = hwConfig->pix_fmt;
        return true;
      }
    }
    return false;
  }

  [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
  private static AVPixelFormat GetFormatCallback(AVCodecContext* ctx, AVPixelFormat* formats)
  {
    for (var p = formats; *p != AVPixelFormat.AV_PIX_FMT_NONE; p++)
    {
      if (*p == _activeHwPixelFormat)
        return *p;
    }
    return formats[0];
  }

  private void Reset()
  {
    if (_swFrame != null)
    {
      var f = _swFrame;
      FFAvUtil.av_frame_free(&f);
      _swFrame = null;
    }

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

    if (_hwDeviceCtx != null)
    {
      var h = _hwDeviceCtx;
      FFAvUtil.av_buffer_unref(&h);
      _hwDeviceCtx = null;
    }

    _hwPixelFormat = AVPixelFormat.AV_PIX_FMT_NONE;
    _activeHwPixelFormat = AVPixelFormat.AV_PIX_FMT_NONE;
    _getFormatCallback = null;
  }

  private static bool IsHardwareFormat(AVPixelFormat fmt) =>
    fmt is AVPixelFormat.AV_PIX_FMT_VULKAN
      or AVPixelFormat.AV_PIX_FMT_VAAPI
      or AVPixelFormat.AV_PIX_FMT_D3D11
      or AVPixelFormat.AV_PIX_FMT_DXVA2_VLD
      or AVPixelFormat.AV_PIX_FMT_VIDEOTOOLBOX
      or AVPixelFormat.AV_PIX_FMT_MEDIACODEC;
}
