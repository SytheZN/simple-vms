namespace Analyzer.MotionGridH26x;

public static class MotionScore
{
  private const int MvActivityGain = 16;
  private const int MvDeadzone = 2;
  private const int ReferenceQstepShift = 11;
  private static readonly int[] QstepScale = [40, 45, 51, 57, 64, 72];

  public static int VisualActivity(int activity, int qp) =>
    (int)((long)activity * (QstepScale[qp % 6] << (qp / 6)) >> ReferenceQstepShift);

  public static int MvTerm(int mv, MotionVectorField field, int index, int sizePenalty)
  {
    var magnitude = Math.Abs((int)(short)mv) + Math.Abs(mv >> 16);
    if (magnitude == 0) return 0;
    if (magnitude <= MvDeadzone << sizePenalty && !field.NeighbourMoving(index)) return 0;
    return (MvActivityGain * magnitude) >> sizePenalty;
  }

  public static int PackMv(int mvx, int mvy) =>
    (mvy << 16) | (mvx & 0xFFFF);

  public static int AddMv(int a, int b) =>
    (((a >> 16) + (b >> 16)) << 16) | (((short)a + (short)b) & 0xFFFF);
}
