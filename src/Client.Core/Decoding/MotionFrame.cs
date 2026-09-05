namespace Client.Core.Decoding;

public sealed record MotionFrame(
  long TimestampUs, byte[] Cells, int Cols, int Rows, bool Sync) : IDecodedItem;
