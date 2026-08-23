namespace ThumbnailBench;

internal static class Stats
{
  public static string Header() =>
    $"  {"",-10} {"mean",8} {"min",8} {"p50",8} {"p95",8} {"max",8}";

  public static string Line(string name, double[] samples)
  {
    var sorted = (double[])samples.Clone();
    Array.Sort(sorted);

    return $"  {name,-10} {samples.Average(),8:F1} {sorted[0],8:F1} " +
      $"{Percentile(sorted, 0.50),8:F1} {Percentile(sorted, 0.95),8:F1} {sorted[^1],8:F1}";
  }

  private static double Percentile(double[] sorted, double fraction) =>
    sorted[Math.Clamp((int)(sorted.Length * fraction), 0, sorted.Length - 1)];
}
