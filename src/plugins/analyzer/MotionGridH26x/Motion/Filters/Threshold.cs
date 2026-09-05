using Shared.Models.Formats;

namespace Analyzer.MotionGridH26x.Filters;

public sealed class Threshold
{
  private int MinActivity { get; init; }
  public Threshold(int threshold)
  {
    if (threshold < 1 || 255 < threshold)
      throw new ArgumentOutOfRangeException(nameof(threshold), threshold, "Threshold must be >0 and <255");
    MinActivity = threshold;
  }

  public MotionGridUnit Push(MotionGridUnit unit)
  {
    var cells = unit.Width * unit.Height;
    var incoming = unit.Data.Span;
    var gated = new byte[cells];

    for (var i = 0; i < cells; i++)
    {
      var value = incoming[i];
      if (value >= MinActivity) gated[i] = value;
    }

    return new MotionGridUnit
    {
      Data = gated,
      Timestamp = unit.Timestamp,
      IsSyncPoint = unit.IsSyncPoint,
      Width = unit.Width,
      Height = unit.Height
    };
  }
}
