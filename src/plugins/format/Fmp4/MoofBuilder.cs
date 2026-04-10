namespace Format.Fmp4;

public record SampleEntry
{
  public required uint Duration { get; init; }
  public required int Size { get; init; }
  public required bool IsKeyframe { get; init; }
  public required int CompositionOffset { get; init; }
}

public static class MoofBuilder
{
  private const uint DefaultSampleFlags = 0x01010000;
  private const uint KeyframeSampleFlags = 0x02000000;

  public static byte[] Build(
    uint sequenceNumber,
    ulong baseDecodeTime,
    IReadOnlyList<SampleEntry> samples,
    ulong wallClockUs = 0,
    int mdatHeaderSize = 8)
  {
    var w = new BoxWriter();
    var len = WriteTo(w, sequenceNumber, baseDecodeTime, samples, wallClockUs, mdatHeaderSize);
    return w.ToArray();
  }

  public static int WriteTo(
    BoxWriter w,
    uint sequenceNumber,
    ulong baseDecodeTime,
    IReadOnlyList<SampleEntry> samples,
    ulong wallClockUs = 0,
    int mdatHeaderSize = 8)
  {
    w.StartBox("moof");

    w.StartFullBox("mfhd", 0, 0);
    w.WriteUInt32(sequenceNumber);
    w.EndBox();

    w.StartBox("traf");

    w.StartFullBox("tfhd", 0, 0x020000);
    w.WriteUInt32(1);
    w.EndBox();

    w.StartFullBox("tfdt", 1, 0);
    w.WriteUInt64(baseDecodeTime);
    w.EndBox();

    var trunFlags = 0x000001u | 0x000100u | 0x000200u | 0x000400u | 0x000800u;
    w.StartFullBox("trun", 0, trunFlags);
    w.WriteUInt32((uint)samples.Count);
    var dataOffsetPos = w.Position;
    w.WriteInt32(0);

    foreach (var sample in samples)
    {
      w.WriteUInt32(sample.Duration);
      w.WriteInt32(sample.Size);
      w.WriteUInt32(sample.IsKeyframe ? KeyframeSampleFlags : DefaultSampleFlags);
      w.WriteInt32(sample.CompositionOffset);
    }

    w.EndBox();
    w.EndBox();

    if (wallClockUs > 0)
    {
      w.StartFullBox("prft", 1, 0);
      w.WriteUInt32(1);
      w.WriteUInt64(wallClockUs);
      w.WriteUInt64(baseDecodeTime);
      w.EndBox();
    }

    w.EndBox();

    var moofLen = w.Length;
    var buf = w.WrittenSpan;
    var dataOffset = moofLen + mdatHeaderSize;
    buf[dataOffsetPos] = (byte)(dataOffset >> 24);
    buf[dataOffsetPos + 1] = (byte)(dataOffset >> 16);
    buf[dataOffsetPos + 2] = (byte)(dataOffset >> 8);
    buf[dataOffsetPos + 3] = (byte)dataOffset;

    return moofLen;
  }
}
