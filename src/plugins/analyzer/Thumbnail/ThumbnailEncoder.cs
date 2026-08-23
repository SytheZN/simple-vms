using BitMiracle.LibJpeg.Classic;

namespace Analyzer.Thumbnail;

internal sealed record EncodedThumbnail(byte[] Data, ushort Width, ushort Height);

/// <summary>
/// Feeds libjpeg YCbCr directly, so no colour matrix is applied on the way in - the decoder's
/// planes are already in the space JPEG stores.
///
/// One encoder serves one stream and is not safe to share between them.
/// </summary>
internal sealed class ThumbnailEncoder
{
  private readonly MemoryStream _buffer = new();
  private readonly jpeg_compress_struct _compressor = new(new jpeg_error_mgr());

  private byte[][] _scanlines = [];
  private int[] _columnStart = [];
  private int[] _columnSpan = [];
  private int[] _chromaColumn = [];
  private int[] _reciprocal = [];
  private int _widestColumn;
  private int _width;
  private int _height;
  private int _sourceWidth;
  private int _sourceHeight;
  private int _quality;

  public EncodedThumbnail Encode(DecodedFrame frame, int boundingSize, int quality)
  {
    var (width, height) = FitWithin(frame.LumaWidth, frame.LumaHeight, boundingSize);

    Resize(frame, width, height);
    Configure(width, height, quality);
    BuildScanlines(frame, width, height);

    _buffer.SetLength(0);

    // libjpeg compresses many images through one object, with the destination re-pointed per image.
    _compressor.jpeg_stdio_dest(_buffer);
    _compressor.jpeg_start_compress(true);
    _compressor.jpeg_write_scanlines(_scanlines, height);
    _compressor.jpeg_finish_compress();

    return new EncodedThumbnail(_buffer.ToArray(), (ushort)width, (ushort)height);
  }

  /// <summary>
  /// Quantisation and Huffman tables outlive the image they were built for.
  /// </summary>
  private void Configure(int width, int height, int quality)
  {
    if (width == _compressor.Image_width && height == _compressor.Image_height
        && quality == _quality)
      return;

    _quality = quality;
    _compressor.Image_width = width;
    _compressor.Image_height = height;
    _compressor.Input_components = 3;
    _compressor.In_color_space = J_COLOR_SPACE.JCS_YCbCr;
    _compressor.jpeg_set_defaults();
    _compressor.jpeg_set_quality(quality, true);
  }

  /// <summary>
  /// Scales so the longest edge meets the bound, preserving aspect. Sources already inside the
  /// bound are left alone rather than upscaled - the tile can stretch a small image itself
  /// without paying for the pixels on the wire.
  /// </summary>
  internal static (int Width, int Height) FitWithin(int width, int height, int bound)
  {
    var longest = Math.Max(width, height);
    if (longest <= bound) return (width, height);

    var scale = (double)bound / longest;
    return (Math.Max(1, (int)Math.Round(width * scale)),
            Math.Max(1, (int)Math.Round(height * scale)));
  }

  /// <summary>Which source columns each output column averages, given the two sizes.</summary>
  private void Resize(DecodedFrame frame, int width, int height)
  {
    if (width == _width && height == _height
        && frame.LumaWidth == _sourceWidth && frame.LumaHeight == _sourceHeight)
      return;

    _width = width;
    _height = height;
    _sourceWidth = frame.LumaWidth;
    _sourceHeight = frame.LumaHeight;

    _scanlines = new byte[height][];
    for (var y = 0; y < height; y++)
      _scanlines[y] = new byte[width * 3];

    _columnStart = new int[width];
    _columnSpan = new int[width];
    _chromaColumn = new int[width];
    _widestColumn = 1;

    for (var x = 0; x < width; x++)
    {
      var start = x * frame.LumaWidth / width;
      var end = Math.Max(start + 1, (x + 1) * frame.LumaWidth / width);
      _columnStart[x] = start;
      _columnSpan[x] = end - start;
      _chromaColumn[x] = start * frame.ChromaWidth / frame.LumaWidth;
      if (end - start > _widestColumn) _widestColumn = end - start;
    }

    _reciprocal = new int[_widestColumn + 1];
  }

  /// <summary>
  /// Box-averages the luma plane into the target size while replicating chroma. The chroma ratio is
  /// taken from the planes rather than assumed to be 2, since a decoder that reduces luma harder
  /// than chroma hands over planes that are already the same size. Nearest-neighbour on chroma is
  /// invisible once the image is this small.
  /// </summary>
  private const int ReciprocalShift = 30;

  private void BuildScanlines(DecodedFrame frame, int width, int height)
  {
    for (var y = 0; y < height; y++)
    {
      var row = _scanlines[y];
      var srcYStart = y * frame.LumaHeight / height;
      var srcYEnd = Math.Max(srcYStart + 1, (y + 1) * frame.LumaHeight / height);
      var rowSpan = srcYEnd - srcYStart;
      var chromaRow = srcYStart * frame.ChromaHeight / frame.LumaHeight * frame.ChromaWidth;

      for (var span = 1; span <= _widestColumn; span++)
        _reciprocal[span] = (int)((1L << ReciprocalShift) / (span * rowSpan)) + 1;

      for (var x = 0; x < width; x++)
      {
        var srcXStart = _columnStart[x];
        var srcXEnd = srcXStart + _columnSpan[x];

        var sum = 0;
        for (var sy = srcYStart; sy < srcYEnd; sy++)
        {
          var offset = sy * frame.LumaWidth;
          for (var sx = srcXStart; sx < srcXEnd; sx++)
            sum += frame.Luma[offset + sx];
        }

        var chromaIndex = chromaRow + _chromaColumn[x];
        var target = x * 3;
        row[target] = (byte)((long)sum * _reciprocal[_columnSpan[x]] >> ReciprocalShift);
        row[target + 1] = frame.Cb[chromaIndex];
        row[target + 2] = frame.Cr[chromaIndex];
      }
    }
  }
}
