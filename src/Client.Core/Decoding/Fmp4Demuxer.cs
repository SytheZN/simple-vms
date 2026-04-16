using System.Buffers.Binary;
using System.Text;

namespace Client.Core.Decoding;

public static class Fmp4Demuxer
{
  public static CodecParameters? ParseInitSegment(ReadOnlySpan<byte> data)
  {
    var moov = FindBox(data, "moov");
    if (moov == null) return null;
    var moovSpan = data[moov.Value.DataOffset..moov.Value.End];

    var trak = FindBox(moovSpan, "trak");
    if (trak == null) return null;
    var trakSpan = moovSpan[trak.Value.DataOffset..trak.Value.End];

    int width = 0, height = 0;
    var tkhd = FindFullBox(moovSpan[..trak.Value.End], trak.Value.DataOffset, "tkhd");
    if (tkhd != null)
    {
      var tkhdDataEnd = tkhd.Value.DataOffset + tkhd.Value.DataSize;
      if (tkhdDataEnd >= 8)
      {
        width = BinaryPrimitives.ReadUInt16BigEndian(moovSpan[(tkhdDataEnd - 8)..]);
        height = BinaryPrimitives.ReadUInt16BigEndian(moovSpan[(tkhdDataEnd - 4)..]);
      }
    }

    var mdia = FindBox(trakSpan, "mdia");
    if (mdia == null) return null;
    var mdiaSpan = trakSpan[mdia.Value.DataOffset..mdia.Value.End];

    var minf = FindBox(mdiaSpan, "minf");
    if (minf == null) return null;
    var minfSpan = mdiaSpan[minf.Value.DataOffset..minf.Value.End];

    var stbl = FindBox(minfSpan, "stbl");
    if (stbl == null) return null;
    var stblSpan = minfSpan[stbl.Value.DataOffset..stbl.Value.End];

    var stsd = FindFullBox(minfSpan[..stbl.Value.End], stbl.Value.DataOffset, "stsd");
    if (stsd == null) return null;

    var entryOffset = stsd.Value.DataOffset + 4;
    if (entryOffset + 8 > stsd.Value.End) return null;

    var stsdSpan = minfSpan;
    var entrySize = (int)BinaryPrimitives.ReadUInt32BigEndian(stsdSpan[entryOffset..]);
    var entryType = ReadFourCc(stsdSpan, entryOffset + 4);

    VideoCodec codec;
    string? configBoxType;
    if (entryType is "avc1" or "avc3")
    {
      codec = VideoCodec.H264;
      configBoxType = "avcC";
    }
    else if (entryType is "hev1" or "hvc1")
    {
      codec = VideoCodec.H265;
      configBoxType = "hvcC";
    }
    else if (entryType is "jpeg" or "mjp2" or "mjpa" or "mjpb")
    {
      codec = entryType == "mjpb" ? VideoCodec.MjpegB : VideoCodec.Mjpeg;
      configBoxType = null;
    }
    else
    {
      return null;
    }

    var entryEnd = entryOffset + entrySize;

    if (configBoxType == null)
      return new CodecParameters(codec, width, height, []);

    var configSearchStart = entryOffset + 8 + 78;
    if (configSearchStart >= entryEnd) return null;

    var configBox = FindBox(stsdSpan[configSearchStart..entryEnd], configBoxType);
    if (configBox == null) return null;

    var descStart = configSearchStart + configBox.Value.DataOffset;
    var descEnd = configSearchStart + configBox.Value.End;
    var extradata = stsdSpan[descStart..descEnd].ToArray();

    return new CodecParameters(codec, width, height, extradata);
  }

  public static uint ParseTimescale(ReadOnlySpan<byte> data)
  {
    var moov = FindBox(data, "moov");
    if (moov == null) return 90000;
    var moovSpan = data[moov.Value.DataOffset..moov.Value.End];

    var trak = FindBox(moovSpan, "trak");
    if (trak == null) return 90000;
    var trakSpan = moovSpan[trak.Value.DataOffset..trak.Value.End];

    var mdia = FindBox(trakSpan, "mdia");
    if (mdia == null) return 90000;
    var mdiaSpan = trakSpan[mdia.Value.DataOffset..mdia.Value.End];

    var mdhd = FindFullBox(trakSpan[..mdia.Value.End], mdia.Value.DataOffset, "mdhd");
    if (mdhd == null) return 90000;

    if (mdhd.Value.Version == 1)
    {
      if (mdhd.Value.DataOffset + 20 > mdia.Value.End) return 90000;
      return BinaryPrimitives.ReadUInt32BigEndian(trakSpan[(mdhd.Value.DataOffset + 16)..]);
    }

    if (mdhd.Value.DataOffset + 12 > mdia.Value.End) return 90000;
    return BinaryPrimitives.ReadUInt32BigEndian(trakSpan[(mdhd.Value.DataOffset + 8)..]);
  }

  public static List<DemuxedSample> DemuxGop(ReadOnlySpan<byte> data, uint timescale)
  {
    var samples = new List<DemuxedSample>();
    long wallClockUs = 0;
    long firstBaseDecodeTime = -1;
    var offset = 0;

    while (offset < data.Length)
    {
      var moof = FindBoxAt(data, offset, "moof");
      if (moof == null) break;

      var moofAbsStart = offset + moof.Value.Offset;
      var moofAbsEnd = offset + moof.Value.End;
      var moofSpan = data[moofAbsStart..moofAbsEnd];

      var traf = FindBox(moofSpan[8..], "traf");
      if (traf == null) { offset = moofAbsEnd; continue; }
      var trafSpan = moofSpan[8..];

      long baseDecodeTime = 0;
      var tfdt = FindFullBox(trafSpan[..traf.Value.End], traf.Value.DataOffset, "tfdt");
      if (tfdt != null)
      {
        if (tfdt.Value.Version == 1 && tfdt.Value.DataOffset + 8 <= traf.Value.End)
          baseDecodeTime = (long)BinaryPrimitives.ReadUInt64BigEndian(trafSpan[tfdt.Value.DataOffset..]);
        else if (tfdt.Value.DataOffset + 4 <= traf.Value.End)
          baseDecodeTime = BinaryPrimitives.ReadUInt32BigEndian(trafSpan[tfdt.Value.DataOffset..]);
      }

      var prft = FindFullBox(moofSpan, 8, "prft");
      if (prft is { Version: 1 } && prft.Value.DataOffset + 12 <= moofSpan.Length)
        wallClockUs = (long)BinaryPrimitives.ReadUInt64BigEndian(moofSpan[(prft.Value.DataOffset + 4)..]);

      var trun = FindFullBox(trafSpan[..traf.Value.End], traf.Value.DataOffset, "trun");
      if (trun == null) { offset = moofAbsEnd; continue; }

      var sampleCount = (int)BinaryPrimitives.ReadUInt32BigEndian(trafSpan[trun.Value.DataOffset..]);
      var flags = trun.Value.Flags;

      var hasDataOffset = (flags & 0x000001) != 0;
      var hasFirstSampleFlags = (flags & 0x000004) != 0;
      var hasDuration = (flags & 0x000100) != 0;
      var hasSize = (flags & 0x000200) != 0;
      var hasSampleFlags = (flags & 0x000400) != 0;
      var hasCtsOffset = (flags & 0x000800) != 0;

      var pos = trun.Value.DataOffset + 4;
      var dataOffset = 0;
      if (hasDataOffset)
      {
        dataOffset = BinaryPrimitives.ReadInt32BigEndian(trafSpan[pos..]);
        pos += 4;
      }

      uint firstSampleFlags = 0;
      if (hasFirstSampleFlags)
      {
        firstSampleFlags = BinaryPrimitives.ReadUInt32BigEndian(trafSpan[pos..]);
        pos += 4;
      }

      if (firstBaseDecodeTime < 0)
        firstBaseDecodeTime = baseDecodeTime;

      var sampleDataOffset = moofAbsStart + dataOffset;
      var currentTime = baseDecodeTime;

      for (var i = 0; i < sampleCount; i++)
      {
        uint duration = 0;
        uint size = 0;
        uint sampleFlags = 0;
        var ctsOffset = 0;

        if (hasDuration) { duration = BinaryPrimitives.ReadUInt32BigEndian(trafSpan[pos..]); pos += 4; }
        if (hasSize) { size = BinaryPrimitives.ReadUInt32BigEndian(trafSpan[pos..]); pos += 4; }
        if (hasSampleFlags) { sampleFlags = BinaryPrimitives.ReadUInt32BigEndian(trafSpan[pos..]); pos += 4; }
        if (hasCtsOffset) { ctsOffset = BinaryPrimitives.ReadInt32BigEndian(trafSpan[pos..]); pos += 4; }

        var effectiveFlags = i == 0 && hasFirstSampleFlags ? firstSampleFlags : sampleFlags;
        var isKey = (effectiveFlags & 0x02000000) != 0;

        // DTS = decode time (no cts offset). PTS = DTS + cts offset. Both
        // expressed as absolute wall-clock microseconds when prft is present.
        var decodeOffsetUs = (currentTime - firstBaseDecodeTime) * 1_000_000L / timescale;
        var ctsOffsetUs = (long)ctsOffset * 1_000_000L / timescale;
        var durationUs = (long)duration * 1_000_000L / timescale;
        var dtsUs = wallClockUs > 0 ? wallClockUs + decodeOffsetUs : 0L;
        var ptsUs = wallClockUs > 0 ? wallClockUs + decodeOffsetUs + ctsOffsetUs : 0L;

        if (sampleDataOffset + size <= data.Length)
        {
          samples.Add(new DemuxedSample(
            data.Slice(sampleDataOffset, (int)size).ToArray(),
            ptsUs,
            dtsUs,
            durationUs,
            isKey));
        }

        sampleDataOffset += (int)size;
        currentTime += duration;
      }

      offset = moofAbsEnd;
      var mdat = FindBoxAt(data, offset, "mdat");
      if (mdat != null) offset += mdat.Value.End;
    }

    return samples;
  }

  private readonly record struct BoxInfo(int Offset, int Size, int DataOffset, int End);

  private readonly record struct FullBoxInfo(
    int Offset, int Size, int DataOffset, int DataSize, int End,
    byte Version, uint Flags);

  private static BoxInfo? FindBox(ReadOnlySpan<byte> data, string type)
  {
    var pos = 0;
    while (pos + 8 <= data.Length)
    {
      var size = (int)BinaryPrimitives.ReadUInt32BigEndian(data[pos..]);
      if (size < 8) return null;
      if (ReadFourCc(data, pos + 4) == type)
        return new BoxInfo(pos, size, pos + 8, pos + size);
      pos += size;
    }
    return null;
  }

  private static BoxInfo? FindBoxAt(ReadOnlySpan<byte> data, int start, string type)
  {
    var pos = start;
    while (pos + 8 <= data.Length)
    {
      var size = (int)BinaryPrimitives.ReadUInt32BigEndian(data[pos..]);
      if (size < 8) return null;
      if (ReadFourCc(data, pos + 4) == type)
        return new BoxInfo(pos - start, size, pos - start + 8, pos - start + size);
      pos += size;
    }
    return null;
  }

  private static FullBoxInfo? FindFullBox(ReadOnlySpan<byte> data, int start, string type)
  {
    var box = FindBox(data[start..], type);
    if (box == null || box.Value.Size < 12) return null;
    var absDataOffset = start + box.Value.DataOffset;
    var version = data[absDataOffset];
    var flags = (uint)((data[absDataOffset + 1] << 16) |
                       (data[absDataOffset + 2] << 8) |
                       data[absDataOffset + 3]);
    var dataOffset = absDataOffset + 4;
    var end = start + box.Value.End;
    return new FullBoxInfo(
      start + box.Value.Offset, box.Value.Size,
      dataOffset, end - dataOffset, end,
      version, flags);
  }

  private static string ReadFourCc(ReadOnlySpan<byte> data, int offset) =>
    Encoding.ASCII.GetString(data.Slice(offset, 4));
}
