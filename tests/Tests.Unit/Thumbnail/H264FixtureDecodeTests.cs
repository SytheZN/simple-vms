using Analyzer.Thumbnail;
using Microsoft.Extensions.Logging;

namespace Tests.Unit.Thumbnail;

/// <summary>
/// Decodes the generated fixtures from scripts/generators/build-h264-fixtures.py. Each enables one
/// coding tool on top of a baseline that has them all off, so a failure names the tool at fault.
/// </summary>
[TestFixture]
public class H264FixtureDecodeTests
{
  private static readonly string[] FixtureNames =
  [
    "plain", "nofilter", "cavlc", "dct8x8", "scalingmatrix", "mbqpdelta", "chromaqp",
    "boundary", "fullhd", "everything"
  ];

  /// <summary>
  /// Matches SCALE in the generator, which writes the reference at that fraction of the displayed
  /// size. It fixes the scale the decoder is judged at, not the scale it emits at.
  /// </summary>
  private const int ReferenceScale = 8;

  /// <summary>
  /// Placeholders. The decoder these gate does not reconstruct a picture yet, so there is nothing
  /// to calibrate against: they are set wide enough to let a working decoder through and must be
  /// ratcheted down to just above the measured mean and worst once it does, the way the H.265
  /// constants were. Leaving them here as-is would make every later change invisible.
  /// </summary>
  private const double FilteredMeanTolerance = 12.0;
  private const int FilteredWorstTolerance = 40;
  private const double UnfilteredMeanTolerance = 12.0;
  private const int UnfilteredWorstTolerance = 40;

  /// <summary>
  /// SCENARIO:
  /// A small all-intra stream using one coding tool beyond the baseline
  ///
  /// ACTION:
  /// Feed the decoder its parameter sets and decode the slice
  ///
  /// EXPECTED RESULT:
  /// A frame is produced, meaning entropy decoding stayed in step to the last macroblock of the
  /// picture
  /// </summary>
  [TestCaseSource(nameof(FixtureNames))]
  public void Decode_Fixture_StaysInSyncToTheEndOfThePicture(string name)
  {
    var rejections = new List<string>();

    Assert.That(Decode(name, rejections), Is.Not.Null, string.Join("; ", rejections));
  }

  /// <summary>
  /// SCENARIO:
  /// Video samples are limited range, and a pixel format ffmpeg treats as full range would have it
  /// rescale every one of them on the way into the reference
  ///
  /// ACTION:
  /// Compare the extremes of each reference plane against the extremes the decoder reconstructs
  ///
  /// EXPECTED RESULT:
  /// Both sit in the same range. A reference stretched to 0..255 against a decode that stops at
  /// 16..235 would make every fidelity number here measure the conversion instead of the decoder
  /// </summary>
  [TestCaseSource(nameof(FixtureNames))]
  public void Fixtures_ReferenceSharesTheDecodersSampleRange(string name)
  {
    var planes = Planes(name);

    Assert.Multiple(() =>
    {
      foreach (var (component, decoded, reference) in planes)
      {
        Assert.That(reference.Min(), Is.EqualTo(decoded.Min()).Within(8),
          $"{component} floor {reference.Min()} against decoded {decoded.Min()}");
        Assert.That(reference.Max(), Is.EqualTo(decoded.Max()).Within(8),
          $"{component} ceiling {reference.Max()} against decoded {decoded.Max()}");
      }
    });
  }

  /// <summary>
  /// SCENARIO:
  /// A reference decode of each fixture, produced by a conformant decoder and downscaled the way
  /// the thumbnail encoder downscales
  ///
  /// ACTION:
  /// Decode the fixture, box-average each plane to the reference size and compare sample by sample
  ///
  /// EXPECTED RESULT:
  /// Every plane tracks its reference. Deblocking is not applied, so block edges carry error, but
  /// it runs after reconstruction rather than feeding it, so the error stays bounded instead of
  /// accumulating along the scan the way a wrong prediction would. The fixture that encodes
  /// without deblocking has nothing left to excuse and must match closely
  /// </summary>
  [TestCaseSource(nameof(FixtureNames))]
  public void Decode_Fixture_ReconstructsTheReferencePicture(string name)
  {
    var filtered = name != "nofilter";
    var planes = Planes(name);

    Assert.Multiple(() =>
    {
      foreach (var (component, decoded, reference) in planes)
      {
        var total = 0;
        var signed = 0;
        var worst = 0;
        var worstAt = 0;
        for (var i = 0; i < reference.Length; i++)
        {
          var difference = decoded[i] - reference[i];
          signed += difference;
          total += Math.Abs(difference);
          if (Math.Abs(difference) <= worst) continue;
          worst = Math.Abs(difference);
          worstAt = i;
        }

        var mean = (double)total / reference.Length;
        var bias = (double)signed / reference.Length;

        Assert.That(mean,
          Is.LessThanOrEqualTo(filtered ? FilteredMeanTolerance : UnfilteredMeanTolerance),
          $"{component} drifts from the reference");
        Assert.That(worst,
          Is.LessThanOrEqualTo(filtered ? FilteredWorstTolerance : UnfilteredWorstTolerance),
          $"{component} sample {worstAt} is {decoded[worstAt]} against {reference[worstAt]}");

        // Error that is off in one direction everywhere is a scaling or rounding fault; error that
        // cancels is the in-loop filtering the decoder omits, which no tolerance here can excuse.
        Assert.That(Math.Abs(bias), Is.LessThan(mean / 2 + 1),
          $"{component} is biased by {bias:F2} against a mean deviation of {mean:F2}");
      }
    });
  }

  /// <summary>
  /// SCENARIO:
  /// A fixture is only meaningful if the encoder actually enabled the tool it is named for
  ///
  /// ACTION:
  /// Parse each fixture's parameter sets and slice header and report the tools they enable
  ///
  /// EXPECTED RESULT:
  /// The baseline has every optional tool off, and each other fixture differs from it. Two tools
  /// are macroblock-layer properties with nothing signalled to read - mb_qp_delta is only visible
  /// as coded values, and whether scaling matrices were sent is walked past rather than kept - so
  /// those fixtures are held to the encoder settings that produced them instead
  /// </summary>
  [Test]
  public void Fixtures_EnableTheToolsTheyAreNamedFor()
  {
    var tools = new Dictionary<string, string>();
    foreach (var name in FixtureNames)
    {
      var (sps, pps, header) = Headers(name);

      tools[name] =
        $"cabac {pps.CabacEnabled} dct8x8 {pps.Transform8x8Mode} " +
        $"chromaQp {pps.ChromaQpIndexOffset} constrained {pps.ConstrainedIntraPred} " +
        $"deblockOff {header.DeblockingFilterDisabled} mbs {sps.WidthInMbs}x{sps.HeightInMbs}";
    }

    Assert.Multiple(() =>
    {
      Assert.That(tools["plain"], Does.Contain("cabac True").And.Contain("dct8x8 False")
        .And.Contain("chromaQp 0").And.Contain("constrained False")
        .And.Contain("deblockOff False"));
      Assert.That(tools["nofilter"], Does.Contain("deblockOff True").And.Contain("cabac True"),
        "the tight reconstruction tolerance only holds if this stream really has no in-loop filter");
      Assert.That(tools["cavlc"], Does.Contain("cabac False"));
      Assert.That(tools["dct8x8"], Does.Contain("dct8x8 True"));
      Assert.That(tools["chromaqp"], Does.Contain("chromaQp 4"));
      Assert.That(tools["everything"], Does.Contain("dct8x8 True").And.Contain("chromaQp 4"));
      Assert.That(tools["boundary"], Does.Contain("mbs 13x9"),
        "200x136 must code as 208x144 so the decoder has to crop");
      Assert.That(tools["fullhd"], Does.Contain("mbs 120x68"),
        "1080 must code as 1088 so the decoder has to crop");
    });
  }

  /// <summary>
  /// SCENARIO:
  /// The decoder implements a subset of AVC, and a stream using anything outside it desynchronises
  /// rather than failing cleanly
  ///
  /// ACTION:
  /// Parse each fixture's parameter sets and slice header, and count its slices
  ///
  /// EXPECTED RESULT:
  /// The picture is frame coded in 4:2:0 and arrives as a single slice, which is what makes taking
  /// the last slice NAL the same thing as taking the whole picture
  /// </summary>
  [TestCaseSource(nameof(FixtureNames))]
  public void Fixtures_UseOnlyImplementedTools(string name)
  {
    var (sps, _, header) = Headers(name);
    var slices = Load($"{name}.h264").Count(IsSlice);

    Assert.Multiple(() =>
    {
      Assert.That(sps.FrameMbsOnly, Is.True, "field and MBAFF coding are not implemented");
      Assert.That(sps.ChromaFormatIdc, Is.EqualTo(1), "only 4:2:0 is implemented");
      Assert.That(slices, Is.EqualTo(1), "a picture split across slices is not implemented");
      Assert.That(header.FirstMbInSlice, Is.Zero);
      Assert.That(header.SliceQp, Is.InRange(0, 51));
      Assert.That(sps.CroppedWidth, Is.LessThanOrEqualTo(sps.WidthInMbs * 16));
      Assert.That(sps.CroppedHeight, Is.LessThanOrEqualTo(sps.HeightInMbs * 16));
    });
  }

  /// <summary>
  /// Each plane reduced to the reference size beside the reference itself. The reference size comes
  /// from the displayed picture rather than the decoded one, so a decoder that emits already-reduced
  /// planes is compared at the same scale as one that emits full size.
  /// </summary>
  private static List<(string Component, byte[] Decoded, byte[] Reference)> Planes(string name)
  {
    var (sps, _, _) = Headers(name);
    var width = sps.CroppedWidth / ReferenceScale;
    var height = sps.CroppedHeight / ReferenceScale;

    var frame = Decode(name, []);
    Assert.That(frame, Is.Not.Null, "decoder produced nothing");

    var planes = new List<(string, byte[], byte[])>
    {
      ("y", Reduce(frame!.Luma, frame.LumaWidth, width, height),
        File.ReadAllBytes(FixturePath($"{name}.y"))),
      ("cb", Reduce(frame.Cb, frame.ChromaWidth, width, height),
        File.ReadAllBytes(FixturePath($"{name}.cb"))),
      ("cr", Reduce(frame.Cr, frame.ChromaWidth, width, height),
        File.ReadAllBytes(FixturePath($"{name}.cr"))),
    };

    foreach (var (component, _, reference) in planes)
      Assert.That(reference, Has.Length.EqualTo(width * height),
        $"{component} reference is not {width}x{height}; regenerate the fixtures");

    return planes;
  }

  /// <summary>
  /// Box-averages whatever the decoder emitted down to the reference grid. Chroma arrives at half
  /// luma resolution, so taking the factor from the plane's own width covers both without the
  /// caller knowing which it holds.
  /// </summary>
  private static byte[] Reduce(byte[] plane, int stride, int width, int height)
  {
    Assert.That(stride, Is.GreaterThanOrEqualTo(width),
      $"a plane {stride} wide cannot be compared against a {width} wide reference");
    Assert.That(stride % width, Is.Zero,
      $"a plane {stride} wide does not reduce evenly onto a {width} wide reference");

    return Downscale(plane, stride, stride / width, width, height);
  }

  /// <summary>
  /// Box-averages a plane, matching how the thumbnail encoder reduces one. The fixture sizes are
  /// chosen so the scale divides both edges exactly.
  /// </summary>
  private static byte[] Downscale(byte[] plane, int stride, int scale, int width, int height)
  {
    var reduced = new byte[width * height];

    for (var y = 0; y < height; y++)
      for (var x = 0; x < width; x++)
      {
        var total = 0;
        for (var sy = 0; sy < scale; sy++)
        {
          var row = (y * scale + sy) * stride + x * scale;
          for (var sx = 0; sx < scale; sx++)
            total += plane[row + sx];
        }

        reduced[y * width + x] = (byte)(total / (scale * scale));
      }

    return reduced;
  }

  private static DecodedFrame? Decode(string name, List<string> rejections)
  {
    var nals = Load($"{name}.h264");

    var decoder = new H264KeyframeDecoder(new CapturingLogger(rejections));
    foreach (var nal in nals.Where(IsParameterSet))
      decoder.AddParameterSet(nal, NalType(nal));

    var slice = nals.Last(IsSlice);
    return decoder.Decode(slice, NalType(slice), RefIdc(slice));
  }

  private static (H264Sps Sps, H264Pps Pps, H264SliceHeader Header) Headers(string name)
  {
    var nals = Load($"{name}.h264");
    var sps = H264Sps.Parse(nals.First(n => NalType(n) == 7));
    var pps = H264Pps.Parse(nals.First(n => NalType(n) == 8));
    var slice = nals.Last(IsSlice);

    var header = H264SliceHeader.Parse(
      Shared.Models.Formats.BitstreamHelpers.ExtractRbsp(slice),
      NalType(slice), RefIdc(slice), sps, pps);

    Assert.That(header, Is.Not.Null, $"{name} slice header did not parse");
    return (sps, pps, header!);
  }

  private sealed class CapturingLogger(List<string> messages) : ILogger
  {
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
      LogLevel logLevel, EventId eventId, TState state, Exception? exception,
      Func<TState, Exception?, string> formatter) =>
      messages.Add(formatter(state, exception));
  }

  private static byte NalType(byte[] nal) => (byte)(nal[0] & 0x1F);

  private static byte RefIdc(byte[] nal) => (byte)((nal[0] >> 5) & 3);

  private static bool IsParameterSet(byte[] nal) => NalType(nal) is 7 or 8;

  private static bool IsSlice(byte[] nal) => NalType(nal) is 1 or 5;

  private static List<byte[]> Load(string name) =>
    AnnexB.Split(File.ReadAllBytes(FixturePath(name)));

  private static string FixturePath(string name) =>
    Path.Combine(AppContext.BaseDirectory, "Thumbnail", "fixtures", "h264", name);
}
