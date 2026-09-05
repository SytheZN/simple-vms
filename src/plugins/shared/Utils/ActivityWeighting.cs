using System.Runtime.CompilerServices;

namespace Utils;

public static class ActivityWeighting
{
  public static int Weigh(int excess, int lowFreq, int highFreq) =>
    (excess << 4) + (lowFreq << 4) + (highFreq << 2);

  [MethodImpl(MethodImplOptions.NoInlining)]
  public static int Weigh(int sum, ReadOnlySpan<byte> positions, int lowFreqLimit)
  {
    var count = positions.Length;
    var lowFreq = 0;
    foreach (var position in positions)
      if (position < lowFreqLimit) lowFreq++;
    return Weigh(sum - count, lowFreq, count - lowFreq);
  }
}
