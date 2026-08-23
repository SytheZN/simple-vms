namespace ThumbnailBench;

internal enum Codec
{
  H265,
  H264,
}

internal sealed record Options(
  Codec Codec, string Frame, int Bound, int Quality, int Iterations, int Warmup)
{
  private const string DefaultVariant = "h265";

  /// <summary>Each names both a decoder and the fixture extension that feeds it.</summary>
  private static readonly string[] Variants = ["h265", "h264b", "h264m", "h264h"];

  public static Options? Parse(string[] args)
  {
    var variant = DefaultVariant;
    string? frame = null;
    var bound = 240;
    var quality = 70;
    var iterations = 50;
    var warmup = 5;

    for (var i = 0; i < args.Length; i++)
    {
      var next = i + 1 < args.Length ? args[i + 1] : null;
      switch (args[i])
      {
        case "--codec" when next != null && Variants.Contains(next): variant = next; i++; break;
        case "--frame" when next != null: frame = next; i++; break;
        case "--bound" when next != null: bound = int.Parse(next); i++; break;
        case "--quality" when next != null: quality = int.Parse(next); i++; break;
        case "--iterations" when next != null: iterations = int.Parse(next); i++; break;
        case "--warmup" when next != null: warmup = int.Parse(next); i++; break;
        default:
          Usage();
          return null;
      }
    }

    var fallback = $"debug/data/keyframe-bench.{variant}";
    frame ??= Locate(fallback);
    if (frame == null)
    {
      Console.Error.WriteLine($"No {fallback} above the working directory; pass --frame");
      return null;
    }

    if (!File.Exists(frame))
    {
      Console.Error.WriteLine($"No such frame: {frame}");
      return null;
    }

    var codec = variant == "h265" ? Codec.H265 : Codec.H264;
    return new Options(codec, frame, bound, quality, iterations, warmup);
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
      $"  --codec <variant> one of {string.Join('|', Variants)} (default: {DefaultVariant})");
    Console.Error.WriteLine(
      "  --frame <path>    annex-b keyframe (default: debug/data/keyframe-bench.<variant>)");
    Console.Error.WriteLine("  --bound <px>      thumbnail bounding size (default: 240)");
    Console.Error.WriteLine("  --quality <1-100> jpeg quality (default: 70)");
    Console.Error.WriteLine("  --iterations <n>  measured iterations (default: 50)");
    Console.Error.WriteLine("  --warmup <n>      unmeasured iterations first (default: 5)");
  }
}
