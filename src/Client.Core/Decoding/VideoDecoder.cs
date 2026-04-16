using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using SimpleVms.FFmpeg.Native;

namespace Client.Core.Decoding;

public sealed unsafe class VideoDecoder : IDisposable
{
  private static readonly AVHWDeviceType[] HwPriority =
  [
    AVHWDeviceType.AV_HWDEVICE_TYPE_VULKAN,
    AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI,
    AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA,
    AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2,
    AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX,
    AVHWDeviceType.AV_HWDEVICE_TYPE_MEDIACODEC,
  ];

  private readonly ILogger _logger;
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

  public VideoDecoder(ILogger logger)
  {
    _logger = logger;
  }

  public bool Configure(CodecParameters config)
  {
    FfmpegLoader.EnsureLoaded();
    _logger.LogDebug("Configuring decoder: {Codec} {Width}x{Height} extradata={ExtradataLen}",
      config.Codec, config.Width, config.Height, config.Extradata.Length);
    Reset();
    _logger.LogDebug("Reset complete");

    var codecId = config.Codec switch
    {
      VideoCodec.H264 => AVCodecID.AV_CODEC_ID_H264,
      VideoCodec.H265 => AVCodecID.AV_CODEC_ID_HEVC,
      VideoCodec.Mjpeg => AVCodecID.AV_CODEC_ID_MJPEG,
      VideoCodec.MjpegB => AVCodecID.AV_CODEC_ID_MJPEGB,
      _ => AVCodecID.AV_CODEC_ID_NONE
    };

    _logger.LogDebug("Finding decoder for {CodecId}", codecId);
    if (codecId == AVCodecID.AV_CODEC_ID_NONE)
    {
      _logger.LogError("Unsupported codec: {Codec}", config.Codec);
      return false;
    }

    var codec = FFAvCodec.avcodec_find_decoder(codecId);
    _logger.LogDebug("avcodec_find_decoder returned {Ptr}", (nint)codec);
    if (codec == null)
    {
      _logger.LogError("FFmpeg decoder not found for {Codec}", config.Codec);
      return false;
    }

    _ctx = FFAvCodec.avcodec_alloc_context3(codec);
    _logger.LogDebug("avcodec_alloc_context3 returned {Ptr}", (nint)_ctx);
    if (_ctx == null)
    {
      _logger.LogError("Failed to allocate codec context");
      return false;
    }

    if (config.Extradata.Length > 0)
    {
      var size = config.Extradata.Length;
      _ctx->extradata = (byte*)FFAvUtil.av_mallocz((nuint)(size + 64));
      _logger.LogDebug("Extradata allocated at {Ptr}, copying {Size} bytes", (nint)_ctx->extradata, size);
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

    _logger.LogDebug("Opening codec...");
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

  public bool SendPacket(DemuxedSample sample)
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

  public bool TryReceiveFrame(out AVFrame* frame)
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

    frame = src;
    return true;
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
  }

  private void TryInitHardwareAccel(AVCodec* codec)
  {
    foreach (var deviceType in HwPriority)
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
    // Return the HW format matching the device context we created. If the
    // decoder doesn't offer it, fall back to the first format (software).
    for (var p = formats; *p != AVPixelFormat.AV_PIX_FMT_NONE; p++)
    {
      if (*p == _activeHwPixelFormat)
        return *p;
    }
    return formats[0];
  }

  private static AVPixelFormat GetHwPixelFormat(AVHWDeviceType deviceType) =>
    deviceType switch
    {
      AVHWDeviceType.AV_HWDEVICE_TYPE_VULKAN => AVPixelFormat.AV_PIX_FMT_VULKAN,
      AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI => AVPixelFormat.AV_PIX_FMT_VAAPI,
      AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA => AVPixelFormat.AV_PIX_FMT_D3D11,
      AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2 => AVPixelFormat.AV_PIX_FMT_DXVA2_VLD,
      AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX => AVPixelFormat.AV_PIX_FMT_VIDEOTOOLBOX,
      AVHWDeviceType.AV_HWDEVICE_TYPE_MEDIACODEC => AVPixelFormat.AV_PIX_FMT_MEDIACODEC,
      _ => AVPixelFormat.AV_PIX_FMT_NONE
    };

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
