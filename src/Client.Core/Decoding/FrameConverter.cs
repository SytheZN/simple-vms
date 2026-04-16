using SimpleVms.FFmpeg.Native;

namespace Client.Core.Decoding;

internal sealed unsafe class FrameConverter : IDisposable
{
  private SwsContext* _ctx;
  private int _srcWidth;
  private int _srcHeight;
  private int _srcFormat;

  public void Convert(AVFrame* src, byte* dst, int dstStride)
  {
    EnsureContext(src->width, src->height, src->format);

    var dstData = stackalloc byte*[1];
    dstData[0] = dst;
    var dstLinesize = stackalloc int[1];
    dstLinesize[0] = dstStride;

    var srcData = stackalloc byte*[8];
    var srcLinesize = stackalloc int[8];
    for (var i = 0; i < 8; i++)
    {
      srcData[i] = src->data[i];
      srcLinesize[i] = src->linesize[i];
    }

    FFSwScale.sws_scale(_ctx, srcData, srcLinesize, 0, src->height, dstData, dstLinesize);
  }

  private void EnsureContext(int width, int height, int format)
  {
    if (_ctx != null && _srcWidth == width && _srcHeight == height && _srcFormat == format)
      return;

    if (_ctx != null)
      FFSwScale.sws_freeContext(_ctx);

    _ctx = FFSwScale.sws_getContext(
      width, height, (AVPixelFormat)format,
      width, height, AVPixelFormat.AV_PIX_FMT_BGRA,
      2, null, null, null);

    _srcWidth = width;
    _srcHeight = height;
    _srcFormat = format;
  }

  public void Dispose()
  {
    if (_ctx != null)
    {
      FFSwScale.sws_freeContext(_ctx);
      _ctx = null;
    }
  }
}
