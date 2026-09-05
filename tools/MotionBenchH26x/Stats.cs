namespace MotionBenchH26x;

internal static class Stats
{
  public static string Header() =>
    $"  {"",-10} {"mean",9} {"min",9} {"p50",9} {"p95",9} {"p99",9} {"max",9}";

  public static string Line(string name, double[] samples)
  {
    var sorted = (double[])samples.Clone();
    Array.Sort(sorted);

    return $"  {name,-10} {samples.Average(),9:F3} {sorted[0],9:F3} " +
      $"{Percentile(sorted, 0.50),9:F3} {Percentile(sorted, 0.95),9:F3} " +
      $"{Percentile(sorted, 0.99),9:F3} {sorted[^1],9:F3}";
  }

  private static double Percentile(double[] sorted, double fraction) =>
    sorted[Math.Clamp((int)(sorted.Length * fraction), 0, sorted.Length - 1)];
}
