using System.Diagnostics;
using Analyzer.MotionGridH26x;
using Shared.Models;
using Shared.Models.Formats;
using Utils;

namespace MotionBenchH26x;

internal interface IBenchExtractor<TNal>
{
  bool TryFeed(TNal unit, out MotionGridUnit? emitted);
  MotionGridUnit? Flush();
  void Observe(PhaseRecorder recorder);
  int DroppedFrames { get; }
  string? LastFailure { get; }
  int MinSliceQp { get; }
  int MaxSliceQp { get; }
}

internal readonly struct H264BenchExtractor(H264SkipExtractor extractor)
  : IBenchExtractor<H264NalUnit>
{
  public bool TryFeed(H264NalUnit unit, out MotionGridUnit? emitted) =>
    extractor.TryFeed(unit, out emitted);
  public MotionGridUnit? Flush() => extractor.Flush();
  public void Observe(PhaseRecorder recorder) => extractor.Observe(recorder);
  public int DroppedFrames => extractor.DroppedFrames;
  public string? LastFailure => extractor.LastFailure;
  public int MinSliceQp => extractor.MinSliceQp;
  public int MaxSliceQp => extractor.MaxSliceQp;
}

internal readonly struct H265BenchExtractor(H265SkipExtractor extractor)
  : IBenchExtractor<H265NalUnit>
{
  public bool TryFeed(H265NalUnit unit, out MotionGridUnit? emitted) =>
    extractor.TryFeed(unit, out emitted);
  public MotionGridUnit? Flush() => extractor.Flush();
  public void Observe(PhaseRecorder recorder) => extractor.Observe(recorder);
  public int DroppedFrames => extractor.DroppedFrames;
  public string? LastFailure => extractor.LastFailure;
  public int MinSliceQp => extractor.MinSliceQp;
  public int MaxSliceQp => extractor.MaxSliceQp;
}

internal static class Bench
{
  public static readonly (string Name, ReconstructionPhase Phase)[] H264Phases =
  [
    ("header", ReconstructionPhase.Header),
    ("last", ReconstructionPhase.Last),
    ("significance", ReconstructionPhase.Significance),
    ("levels", ReconstructionPhase.Levels)
  ];

  public static readonly (string Name, ReconstructionPhase Phase)[] H265Phases =
  [
    ("header", ReconstructionPhase.Header),
    ("sao", ReconstructionPhase.Sao),
    ("last", ReconstructionPhase.Last),
    ("significance", ReconstructionPhase.Significance),
    ("levels", ReconstructionPhase.Levels)
  ];

  public static void Run<TNal, TExtractor>(
    string segment, Options options, Source<TNal> source, TExtractor extractor,
    (string Name, ReconstructionPhase Phase)[] phases)
    where TNal : IDataUnit
    where TExtractor : struct, IBenchExtractor<TNal>
  {
    var processor = new MotionGridProcessor(new ProcessorSettings("none", 10, false, false), () => null, new ConsoleLogger());
    var recorder = new PhaseRecorder();
    extractor.Observe(recorder);

    var warmed = 0;
    foreach (var unit in source.Feed)
    {
      if (!unit.IsHeader && ++warmed > options.WarmupSlices) break;
      try { extractor.TryFeed(unit, out _); }
      catch { }
    }

    var warmupDropped = extractor.DroppedFrames;
    var slices = source.Feed.Count(unit => !unit.IsHeader);
    var feedMs = new double[slices];
    var grids = new List<MotionGridUnit>();
    var errors = 0;
    string? firstError = null;

    recorder.Reset();
    GC.Collect();
    GC.WaitForPendingFinalizers();
    var gen0 = GC.CollectionCount(0);
    var gen1 = GC.CollectionCount(1);
    var gen2 = GC.CollectionCount(2);
    var allocated = GC.GetAllocatedBytesForCurrentThread();

    var sample = 0;
    foreach (var unit in source.Feed)
    {
      if (unit.IsHeader)
      {
        extractor.TryFeed(unit, out _);
        continue;
      }

      var started = Stopwatch.GetTimestamp();
      MotionGridUnit? grid = null;
      try
      {
        extractor.TryFeed(unit, out grid);
      }
      catch (Exception ex)
      {
        errors++;
        firstError ??= ex.Message;
      }
      feedMs[sample++] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
      if (grid != null)
      {
        processor.Feed(grid);
        while (processor.TryReceive(out var processed)) grids.Add(processed);
      }
    }
    var flushed = extractor.Flush();
    if (flushed != null) processor.Feed(flushed);
    processor.Flush();
    while (processor.TryReceive(out var processed)) grids.Add(processed);

    allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
    gen0 = GC.CollectionCount(0) - gen0;
    gen1 = GC.CollectionCount(1) - gen1;
    gen2 = GC.CollectionCount(2) - gen2;

    var totalMs = feedMs.Sum();
    var duration = source.DurationSeconds;
    var fps = duration > 0 ? (source.Frames + source.Keyframes) / duration : 0;
    var load = duration > 0 ? totalMs / 1000 / duration : 0;

    Console.WriteLine();
    Console.WriteLine($"{Path.GetFileName(segment)}  {source.Bytes / 1024 / 1024}MB  " +
      $"~{duration:F1}s at {fps:F1} fps");
    Console.WriteLine($"fed {slices} slices in {source.Frames} p/b frames " +
      $"({source.FedBytes / 1024 / 1024}MB), {source.Keyframes} keyframes");
    Console.WriteLine($"{grids.Count} grids emitted, " +
      $"{extractor.DroppedFrames - warmupDropped} frames dropped" +
      (extractor.LastFailure == null ? "" : $" (last: {extractor.LastFailure})") +
      $", {errors} errors" + (firstError == null ? "" : $" (first: {firstError})"));
    Console.WriteLine($"{recorder.Coded / source.Frames}/{recorder.Blocks / source.Frames} " +
      "coded/blocks per frame");
    Console.WriteLine($"slice qp {extractor.MinSliceQp}..{extractor.MaxSliceQp}");
    Console.WriteLine($"{Build()}, clock {recorder.OverheadNanoseconds:F0}ns");
    Console.WriteLine();
    Console.WriteLine(Stats.Header());
    Console.WriteLine(Stats.Line("feed", feedMs));
    Console.WriteLine();

    var accounted = 0.0;
    foreach (var (name, phase) in phases)
    {
      var ms = recorder.Ms(phase);
      accounted += ms;
      Phase(name, ms, totalMs);
    }
    Phase("walk", totalMs - accounted, totalMs);
    Console.WriteLine();
    Console.WriteLine($"  {"total",-13} {totalMs / 1000,8:F2} s   " +
      $"{load * 100,5:F1}% of one core at live pace   x32 streams: {load * 32 * 100,5:F1}%");
    Console.WriteLine($"  {"alloc",-13} {allocated / (double)slices / 1024,8:F1} KB per slice" +
      $"   gen0 {gen0}  gen1 {gen1}  gen2 {gen2}");
    Console.WriteLine();

    if (grids.Count > 0)
    {
      var video = segment + ".grid.mp4";
      var gridFps = duration > 0 ? grids.Count / duration : 0;
      var failure = GridEncoder.Encode(grids, gridFps, video);
      Console.WriteLine(failure == null
        ? $"{grids.Count} grids ({grids[0].Width}x{grids[0].Height}) -> {video}"
        : $"grid video failed: {failure}");
      Console.WriteLine();
    }
  }

  private static void Phase(string name, double ms, double total) =>
    Console.WriteLine($"  {name,-13} {ms,8:F1} ms  {ms / total * 100,5:F1}%");

  private static string Build() =>
#if DEBUG
    "DEBUG build";
#else
    "RELEASE build";
#endif
}
