using System.Diagnostics;
using System.Globalization;
using Analyzer.Thumbnail;
using ThumbnailBench;
using Utils;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

var options = Options.Parse(args);
if (options == null) return 1;

var logger = new ConsoleLogger();
var source = Source.Open(options, logger);
if (source == null) return 1;

var probe = source.Decode();
if (probe == null)
{
  Console.Error.WriteLine("Decode rejected the frame; see the warnings above");
  return 1;
}

var encoder = new ThumbnailEncoder();

for (var i = 0; i < options.Warmup; i++)
{
  source.Decode();
  encoder.Encode(probe, options.Bound, options.Quality);
}

var decodeMs = new double[options.Iterations];
var encodeMs = new double[options.Iterations];

var recorder = new PhaseRecorder();
source.Observe?.Invoke(recorder);
recorder.Reset();

GC.Collect();
GC.WaitForPendingFinalizers();
var gen0 = GC.CollectionCount(0);
var gen1 = GC.CollectionCount(1);
var gen2 = GC.CollectionCount(2);
var allocated = GC.GetAllocatedBytesForCurrentThread();

for (var i = 0; i < options.Iterations; i++)
{
  var started = Stopwatch.GetTimestamp();
  var frame = source.Decode();
  var decoded = Stopwatch.GetTimestamp();
  encoder.Encode(frame!, options.Bound, options.Quality);
  var encoded = Stopwatch.GetTimestamp();

  decodeMs[i] = Stopwatch.GetElapsedTime(started, decoded).TotalMilliseconds;
  encodeMs[i] = Stopwatch.GetElapsedTime(decoded, encoded).TotalMilliseconds;
}

allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
gen0 = GC.CollectionCount(0) - gen0;
gen1 = GC.CollectionCount(1) - gen1;
gen2 = GC.CollectionCount(2) - gen2;

var jpeg = encoder.Encode(probe, options.Bound, options.Quality);
var runs = options.Iterations;

var preview = options.Frame + ".jpg";
File.WriteAllBytes(preview, jpeg.Data);

Console.WriteLine();
Console.WriteLine($"{Path.GetFileName(options.Frame)}  {source.Bytes / 1024}KB  " +
  $"-> {probe.LumaWidth}x{probe.LumaHeight} planes -> {jpeg.Width}x{jpeg.Height} jpeg q{options.Quality}" +
  $" -> {preview}");
Console.WriteLine($"{recorder.Coded / runs}/{recorder.Blocks / runs} blocks per picture");
Console.WriteLine($"{runs} iterations after {options.Warmup} warmup, {Build()}, " +
  $"clock {recorder.OverheadNanoseconds:F0}ns");
Console.WriteLine();
Console.WriteLine(Stats.Header());
Console.WriteLine(Stats.Line("decode", decodeMs));
Console.WriteLine(Stats.Line("encode", encodeMs));
Console.WriteLine();

var total = decodeMs.Average();
var header = recorder.Ms(ReconstructionPhase.Header) / runs;
var gather = recorder.Ms(ReconstructionPhase.Gather) / runs;
var smooth = recorder.Ms(ReconstructionPhase.Smooth) / runs;
var predict = recorder.Ms(ReconstructionPhase.Predict) / runs;
var last = recorder.Ms(ReconstructionPhase.Last) / runs;
var significance = recorder.Ms(ReconstructionPhase.Significance) / runs;
var levels = recorder.Ms(ReconstructionPhase.Levels) / runs;
var emit = recorder.Ms(ReconstructionPhase.Emit) / runs;
var edge = recorder.Ms(ReconstructionPhase.Edge) / runs;
var cellsPhase = recorder.Ms(ReconstructionPhase.Cells) / runs;
var samples = recorder.Ms(ReconstructionPhase.Samples) / runs;
var write = recorder.Ms(ReconstructionPhase.Write) / runs;

Phase("header", header, total);
Phase("gather", gather, total);
Phase("smooth", smooth, total);
Phase("predict", predict, total);
Phase("last", last, total);
Phase("significance", significance, total);
Phase("levels", levels, total);
Phase("emit", emit, total);
Phase("edge", edge, total);
Phase("cells", cellsPhase, total);
Phase("samples", samples, total);
Phase("write", write, total);
Phase("walk",
  total - header - gather - smooth - predict - last - significance - levels - emit
    - edge - cellsPhase - samples - write,
  total);
Console.WriteLine();
Console.WriteLine($"  {"alloc",-13} {allocated / (double)runs / 1024,8:F1} KB per iteration" +
  $"   gen0 {gen0}  gen1 {gen1}  gen2 {gen2}");
Console.WriteLine();
return 0;

static void Phase(string name, double ms, double total) =>
  Console.WriteLine($"  {name,-13} {ms,8:F1} ms  {ms / total * 100,5:F1}%");

static string Build() =>
#if DEBUG
  "DEBUG build";
#else
  "RELEASE build";
#endif
