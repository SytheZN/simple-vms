namespace MotionBenchH26x;

internal sealed record Options(IReadOnlyList<string> Segments, int WarmupSlices, int AverageFrames)
{
  private static readonly string[] DefaultSegments =
  [
    "debug/motion-bench.h264m.mp4",
    "debug/motion-bench.h265.mp4"
  ];

  public static Options? Parse(string[] args)
  {
    var segments = new List<string>();
    var warmupSlices = 5;
    var averageFrames = 10;

    for (var i = 0; i < args.Length; i++)
    {
      var next = i + 1 < args.Length ? args[i + 1] : null;
      switch (args[i])
      {
        case "--segment" when next != null: segments.Add(next); i++; break;
        case "--warmup" when next != null: warmupSlices = int.Parse(next); i++; break;
        case "--average" when next != null: averageFrames = int.Parse(next); i++; break;
        default:
          Usage();
          return null;
      }
    }

    if (segments.Count == 0)
    {
      segments.AddRange(DefaultSegments.Select(Locate).OfType<string>());
      if (segments.Count == 0)
      {
        Console.Error.WriteLine(
          $"No {string.Join(" or ", DefaultSegments)} above the working directory; pass --segment");
        return null;
      }
    }

    foreach (var segment in segments)
    {
      if (!File.Exists(segment))
      {
        Console.Error.WriteLine($"No such segment: {segment}");
        return null;
      }
    }

    return new Options(segments, warmupSlices, averageFrames);
  }

  private static string? Locate(string relative)
  {
    for (var dir = new DirectoryInfo(Environment.CurrentDirectory); dir != null; dir = dir.Parent)
    {
      var candidate = Path.Combine(dir.FullName, relative);
      if (File.Exists(candidate)) return candidate;
    }
    return null;
  }

  private static void Usage()
  {
    Console.Error.WriteLine("Usage: dotnet run -c Release -- [options]");
    Console.Error.WriteLine(
      "  --segment <path>  fMP4 recording segment, repeatable; codec is detected from the file");
    Console.Error.WriteLine(
      $"                    (default: every one of {string.Join(", ", DefaultSegments)} found above the working directory)");
    Console.Error.WriteLine("  --warmup <n>      unmeasured slices fed first (default: 5)");
    Console.Error.WriteLine("  --average <n>     rolling average window in frames, 1 = off (default: 10)");
  }
}
