using Analyzer.Thumbnail;
using Microsoft.Extensions.Logging;

namespace Tests.Unit.Thumbnail;

/// <summary>
/// Decodes the generated fixtures from scripts/generators/build-h265-fixtures.py. Each enables one
/// coding tool on top of a baseline that has them all off, so a failure names the tool at fault.
/// </summary>
[TestFixture]
public class H265FixtureDecodeTests
{
  private static readonly string[] FixtureNames =
  [
    "plain", "nofilter", "sao", "cuqpdelta", "transformskip", "signdatahiding", "transformdepth",
    "boundary", "largeunits", "fullhd", "everything"
  ];

  /// <summary>
  /// Matches SCALE in the generator, which writes the reference at that fraction of the encoded
  /// size. It fixes the scale the decoder is judged at, not the scale it emits at: reducing the
  /// picture is the decoder's own job to take over, and until it does the tests close the gap.
  /// </summary>
  private const int ReferenceScale = 8;

  /// <summary>
  /// What deblocking costs a decoder that does not apply it. The fixture encoded without the
  /// filter has no such excuse and is held to the unfiltered pair instead.
  ///
  /// These sit just above what the decoder currently achieves, so a change that degrades the
  /// picture fails here rather than passing inside slack. Any stage that is meant to be bit-exact
  /// must leave them untouched.
  ///
  /// They are wider than a full resolution decode needs because the decoder emits reduced: a block
  /// is carried into the picture by its average over each output sample, which is exact for the
  /// frequencies that survive the reduction and drops the rest.
  ///
  /// The worst-sample bounds are wider than the mean ones need because chroma takes the same
  /// reduction as luma, leaving it subsampled the way the encoder wants it. That moves individual
  /// samples on a sharp chroma edge without moving the aggregate, which is why only these two
  /// carry the slack.
  /// </summary>
  private const double FilteredMeanTolerance = 0.58;
  private const int FilteredWorstTolerance = 10;
  private const double UnfilteredMeanTolerance = 0.31;
  private const int UnfilteredWorstTolerance = 6;

  /// <summary>
  /// SCENARIO:
  /// A small all-intra stream using one coding tool beyond the baseline
  ///
  /// ACTION:
  /// Feed the decoder its parameter sets and decode the slice
  ///
  /// EXPECTED RESULT:
  /// A frame is produced, meaning CABAC stayed in step to the last CTB of the picture
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
  /// Each plane reduced to the reference size beside the reference itself. The reference size comes
  /// from the encoded picture rather than the decoded one, so a decoder that emits already-reduced
  /// planes is compared at the same scale as one that emits full size, and a decoder that emits at
  /// the reference scale is compared directly.
  /// </summary>
  private static List<(string Component, byte[] Decoded, byte[] Reference)> Planes(string name)
  {
    var nals = Load($"{name}.h265");
    var sps = H265Sps.Parse(nals.First(n => NalType(n) == 33))!;
    var width = sps.Width / ReferenceScale;
    var height = sps.Height / ReferenceScale;

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
  /// SCENARIO:
  /// A fixture is only meaningful if the encoder actually enabled the tool it is named for
  ///
  /// ACTION:
  /// Parse each fixture's parameter sets and report the tools they enable
  ///
  /// EXPECTED RESULT:
  /// The baseline has every optional tool off, and each other fixture differs from it
  /// </summary>
  [Test]
  public void Fixtures_EnableTheToolsTheyAreNamedFor()
  {
    var tools = new Dictionary<string, string>();
    foreach (var name in FixtureNames)
    {
      var nals = Load($"{name}.h265");
      var sps = H265Sps.Parse(nals.First(n => NalType(n) == 33))!;
      var pps = H265Pps.Parse(nals.First(n => NalType(n) == 34));

      tools[name] =
        $"sao {sps.SaoEnabled} depthIntra {sps.MaxTransformHierarchyDepthIntra} " +
        $"cuQpDelta {pps.CuQpDeltaEnabled} tskip {pps.TransformSkipEnabled} " +
        $"sdh {pps.SignDataHiding} deblockOff {pps.DeblockingFilterDisabled}";
    }

    Assert.Multiple(() =>
    {
      Assert.That(tools["plain"], Does.Contain("sao False").And.Contain("cuQpDelta False")
        .And.Contain("tskip False").And.Contain("sdh False").And.Contain("depthIntra 0")
        .And.Contain("deblockOff False"));
      Assert.That(tools["nofilter"], Does.Contain("deblockOff True").And.Contain("sao False"),
        "the tight reconstruction tolerance only holds if this stream really has no in-loop filter");
      Assert.That(tools["sao"], Does.Contain("sao True"));
      Assert.That(tools["cuqpdelta"], Does.Contain("cuQpDelta True"));
      Assert.That(tools["transformskip"], Does.Contain("tskip True"));
      Assert.That(tools["signdatahiding"], Does.Contain("sdh True"));
      Assert.That(tools["transformdepth"], Does.Not.Contain("depthIntra 0"));
    });
  }

  /// <summary>
  /// SCENARIO:
  /// The decoder implements a subset of HEVC, and a stream using anything outside it desynchronises
  /// rather than failing cleanly
  ///
  /// ACTION:
  /// Parse each fixture's parameter sets and slice header
  ///
  /// EXPECTED RESULT:
  /// Neither tiles nor wavefront entropy sync is in use, since both change how CABAC is initialised
  /// and terminated across the picture, and the coded picture is described consistently
  /// </summary>
  [TestCaseSource(nameof(FixtureNames))]
  public void Fixtures_UseOnlyImplementedTools(string name)
  {
    var nals = Load($"{name}.h265");
    var sps = H265Sps.Parse(nals.First(n => NalType(n) == 33))!;
    var pps = H265Pps.Parse(nals.First(n => NalType(n) == 34));
    var slice = nals.Last(n => !IsParameterSet(n));
    var header = H265SliceHeader.Parse(
      Utils.BitstreamHelpers.ExtractRbsp(slice), NalType(slice), sps, pps);

    Assert.Multiple(() =>
    {
      Assert.That(pps.TilesEnabled, Is.False, "tiles are not implemented");
      Assert.That(pps.EntropyCodingSyncEnabled, Is.False, "wavefront sync is not implemented");
      Assert.That(sps.ChromaFormatIdc, Is.EqualTo(1), "only 4:2:0 is implemented");
      Assert.That(sps.Log2CtbSize, Is.InRange(4, 6));
      Assert.That(sps.CodedWidth, Is.GreaterThanOrEqualTo(sps.Width));
      Assert.That(sps.CodedHeight, Is.GreaterThanOrEqualTo(sps.Height));
      Assert.That(sps.Log2MinTbSize, Is.LessThanOrEqualTo(sps.Log2MaxTbSize));
      Assert.That(header, Is.Not.Null, "slice header did not parse");
      Assert.That(header!.SliceQp, Is.InRange(0, 51));
    });
  }

  private static DecodedFrame? Decode(string name, List<string> rejections)
  {
    var nals = Load($"{name}.h265");

    var decoder = new H265KeyframeDecoder(new CapturingLogger(rejections));
    foreach (var nal in nals.Where(IsParameterSet))
      decoder.AddParameterSet(nal, NalType(nal));

    var slice = nals.Last(n => !IsParameterSet(n));
    return decoder.Decode(slice, NalType(slice), MaximumReduction);
  }

  /// <summary>
  /// A bounding size no picture can meet, so the decoder reduces as far as it is willing to. That
  /// is what a 1080p camera gets in practice and it is the least forgiving case, since the smallest
  /// transform blocks collapse to a single sample.
  /// </summary>
  private const int MaximumReduction = 1;

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

  private sealed class CapturingLogger(List<string> messages) : ILogger
  {
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
      LogLevel logLevel, EventId eventId, TState state, Exception? exception,
      Func<TState, Exception?, string> formatter) =>
      messages.Add(formatter(state, exception));
  }

  private static byte NalType(byte[] nal) => AnnexB.NalType(nal);

  private static bool IsParameterSet(byte[] nal) => AnnexB.IsParameterSet(nal);

  private static List<byte[]> Load(string name) =>
    AnnexB.Split(File.ReadAllBytes(FixturePath(name)));

  private static string FixturePath(string name) =>
    Path.Combine(AppContext.BaseDirectory, "Thumbnail", "fixtures", "h265", name);
}
