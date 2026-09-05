namespace Analyzer.MotionGridH26x;

internal static class BMbTypes
{
  public const int Direct16x16 = 0;
  public const int EightByEight = 22;
  public const int IntraOffset = 23;

  public const byte ListL0 = 1;
  public const byte ListL1 = 2;
  public const byte ListBi = 3;

  public const int SubDirect = 0;

  public static readonly byte[][] PartitionLists =
  [
    [ListL0, ListL0], [ListL1, ListL1], [ListL0, ListL1], [ListL1, ListL0],
    [ListL0, ListBi], [ListL1, ListBi], [ListBi, ListL0], [ListBi, ListL1],
    [ListBi, ListBi],
  ];

  public static byte[] ListsFor(int mbType) =>
    PartitionLists[(mbType - 4) >> 1];

  public static bool IsVerticalSplit(int mbType) => (mbType & 1) == 1;

  public static readonly byte[] SubLists =
    [0, ListL0, ListL1, ListBi, ListL0, ListL0, ListL1, ListL1, ListBi, ListBi, ListL0, ListL1, ListBi];

  public static readonly byte[] SubPartCellsW = [2, 2, 2, 2, 2, 1, 2, 1, 2, 1, 1, 1, 1];
  public static readonly byte[] SubPartCellsH = [2, 2, 2, 2, 1, 2, 1, 2, 1, 2, 1, 1, 1];
}
