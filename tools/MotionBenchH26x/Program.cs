using System.Globalization;
using Analyzer.MotionGridH26x;
using MotionBenchH26x;
using Shared.Models.Formats;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

var options = Options.Parse(args);
if (options == null) return 1;

var failed = false;
foreach (var segment in options.Segments)
{
  switch (await Source.Open(segment))
  {
    case Source<H264NalUnit> h264:
      Bench.Run(segment, options, h264,
        new H264BenchExtractor(new H264SkipExtractor(new ConsoleLogger())), Bench.H264Phases);
      break;
    case Source<H265NalUnit> h265:
      Bench.Run(segment, options, h265,
        new H265BenchExtractor(new H265SkipExtractor(new ConsoleLogger())), Bench.H265Phases);
      break;
    default:
      failed = true;
      break;
  }
}
return failed ? 1 : 0;
