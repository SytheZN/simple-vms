using Analyzer.Thumbnail;
using Microsoft.Extensions.Logging;
using Utils;

namespace ThumbnailBench;

internal sealed record Source(
  Func<DecodedFrame?> Decode, Action<IObserverHarness<ReconstructionPhase>>? Observe, int Bytes)
{
  public static Source? Open(Options options, ILogger logger)
  {
    var stream = File.ReadAllBytes(options.Frame);
    var units = AnnexB.Split(stream);

    var source = options.Codec == Codec.H264
      ? OpenH264(options, logger, units, stream.Length)
      : OpenH265(options, logger, units, stream.Length);

    if (source == null)
      Console.Error.WriteLine(
        $"{options.Frame} holds no {options.Codec} slice NAL, only {units.Count} units");

    return source;
  }

  private static Source? OpenH265(Options options, ILogger logger, List<byte[]> units, int bytes)
  {
    var decoder = new H265KeyframeDecoder(logger);
    byte[]? slice = null;
    byte sliceType = 0;

    foreach (var unit in units)
    {
      var type = AnnexB.Type(unit, Codec.H265);
      if (type is 32 or 33 or 34)
      {
        decoder.AddParameterSet(unit, type);
        continue;
      }

      if (slice != null || type >= 32) continue;
      slice = unit;
      sliceType = type;
    }

    return slice == null
      ? null
      : new Source(() => decoder.Decode(slice, sliceType, options.Bound), decoder.Observe, bytes);
  }

  private static Source? OpenH264(Options options, ILogger logger, List<byte[]> units, int bytes)
  {
    var decoder = new H264KeyframeDecoder(logger);
    byte[]? slice = null;
    byte sliceType = 0;
    byte refIdc = 0;

    foreach (var unit in units)
    {
      var type = AnnexB.Type(unit, Codec.H264);
      if (type is 7 or 8)
      {
        decoder.AddParameterSet(unit, type);
        continue;
      }

      if (slice != null || type is not (1 or 5)) continue;
      slice = unit;
      sliceType = type;
      refIdc = AnnexB.RefIdc(unit);
    }

    return slice == null
      ? null
      : new Source(() => decoder.Decode(slice, sliceType, refIdc), decoder.Observe, bytes);
  }
}
