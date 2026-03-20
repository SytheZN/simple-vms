namespace Format.Fmp4;

public static class MoovBuilder
{
  private const uint DefaultSampleFlags = 0x01010000;

  public static byte[] BuildH264(int width, int height, uint timescale, byte[] avcC)
  {
    return Build(width, height, timescale, "avc1", avcC);
  }

  public static byte[] BuildH265(int width, int height, uint timescale, byte[] hvcC)
  {
    return Build(width, height, timescale, "hev1", hvcC);
  }

  private static byte[] Build(int width, int height, uint timescale, string sampleEntryType, byte[] codecConfig)
  {
    var codecConfigBoxType = sampleEntryType == "avc1" ? "avcC" : "hvcC";

    var w = new BoxWriter();
    w.StartBox("moov");

    WriteMvhd(w, timescale);
    WriteTrak(w, width, height, timescale, sampleEntryType, codecConfigBoxType, codecConfig);
    WriteMvex(w);

    w.EndBox();
    return w.ToArray();
  }

  private static void WriteMvhd(BoxWriter w, uint timescale)
  {
    w.StartFullBox("mvhd", 0, 0);
    w.WriteUInt32(0);
    w.WriteUInt32(0);
    w.WriteUInt32(timescale);
    w.WriteUInt32(0);
    w.WriteUInt32(0x00010000);
    w.WriteUInt16(0x0100);
    w.WriteZeros(10);
    WriteIdentityMatrix(w);
    w.WriteZeros(24);
    w.WriteUInt32(2);
    w.EndBox();
  }

  private static void WriteTrak(BoxWriter w, int width, int height, uint timescale,
    string sampleEntryType, string codecConfigBoxType, byte[] codecConfig)
  {
    w.StartBox("trak");
    WriteTkhd(w, width, height);
    WriteMdia(w, width, height, timescale, sampleEntryType, codecConfigBoxType, codecConfig);
    w.EndBox();
  }

  private static void WriteTkhd(BoxWriter w, int width, int height)
  {
    w.StartFullBox("tkhd", 0, 0x000003);
    w.WriteUInt32(0);
    w.WriteUInt32(0);
    w.WriteUInt32(1);
    w.WriteZeros(4);
    w.WriteUInt32(0);
    w.WriteZeros(8);
    w.WriteUInt16(0);
    w.WriteUInt16(0);
    w.WriteUInt16(0);
    w.WriteUInt16(0);
    WriteIdentityMatrix(w);
    w.WriteUInt32((uint)(width << 16));
    w.WriteUInt32((uint)(height << 16));
    w.EndBox();
  }

  private static void WriteMdia(BoxWriter w, int width, int height, uint timescale,
    string sampleEntryType, string codecConfigBoxType, byte[] codecConfig)
  {
    w.StartBox("mdia");
    WriteMdhd(w, timescale);
    WriteHdlr(w);
    WriteMinf(w, width, height, sampleEntryType, codecConfigBoxType, codecConfig);
    w.EndBox();
  }

  private static void WriteMdhd(BoxWriter w, uint timescale)
  {
    w.StartFullBox("mdhd", 0, 0);
    w.WriteUInt32(0);
    w.WriteUInt32(0);
    w.WriteUInt32(timescale);
    w.WriteUInt32(0);
    w.WriteUInt16(0x55C4);
    w.WriteUInt16(0);
    w.EndBox();
  }

  private static void WriteHdlr(BoxWriter w)
  {
    w.StartFullBox("hdlr", 0, 0);
    w.WriteUInt32(0);
    w.WriteFourCc("vide");
    w.WriteZeros(12);
    w.WriteUInt8(0);
    w.EndBox();
  }

  private static void WriteMinf(BoxWriter w, int width, int height,
    string sampleEntryType, string codecConfigBoxType, byte[] codecConfig)
  {
    w.StartBox("minf");

    w.StartFullBox("vmhd", 0, 1);
    w.WriteUInt16(0);
    w.WriteUInt16(0);
    w.WriteUInt16(0);
    w.WriteUInt16(0);
    w.EndBox();

    w.StartBox("dinf");
    w.StartFullBox("dref", 0, 0);
    w.WriteUInt32(1);
    w.StartFullBox("url ", 0, 1);
    w.EndBox();
    w.EndBox();
    w.EndBox();

    WriteStbl(w, width, height, sampleEntryType, codecConfigBoxType, codecConfig);

    w.EndBox();
  }

  private static void WriteStbl(BoxWriter w, int width, int height,
    string sampleEntryType, string codecConfigBoxType, byte[] codecConfig)
  {
    w.StartBox("stbl");

    w.StartFullBox("stsd", 0, 0);
    w.WriteUInt32(1);

    w.StartBox(sampleEntryType);
    w.WriteZeros(6);
    w.WriteUInt16(1);
    w.WriteZeros(16);
    w.WriteUInt16((ushort)width);
    w.WriteUInt16((ushort)height);
    w.WriteUInt32(0x00480000);
    w.WriteUInt32(0x00480000);
    w.WriteZeros(4);
    w.WriteUInt16(1);
    w.WriteZeros(32);
    w.WriteUInt16(0x0018);
    w.WriteInt32(-1);

    w.StartBox(codecConfigBoxType);
    w.WriteBytes(codecConfig);
    w.EndBox();

    w.EndBox();
    w.EndBox();

    w.StartFullBox("stts", 0, 0);
    w.WriteUInt32(0);
    w.EndBox();

    w.StartFullBox("stsc", 0, 0);
    w.WriteUInt32(0);
    w.EndBox();

    w.StartFullBox("stsz", 0, 0);
    w.WriteUInt32(0);
    w.WriteUInt32(0);
    w.EndBox();

    w.StartFullBox("stco", 0, 0);
    w.WriteUInt32(0);
    w.EndBox();

    w.EndBox();
  }

  private static void WriteMvex(BoxWriter w)
  {
    w.StartBox("mvex");
    w.StartFullBox("trex", 0, 0);
    w.WriteUInt32(1);
    w.WriteUInt32(1);
    w.WriteUInt32(0);
    w.WriteUInt32(0);
    w.WriteUInt32(DefaultSampleFlags);
    w.EndBox();
    w.EndBox();
  }

  private static void WriteIdentityMatrix(BoxWriter w)
  {
    w.WriteUInt32(0x00010000);
    w.WriteZeros(4);
    w.WriteZeros(4);
    w.WriteZeros(4);
    w.WriteUInt32(0x00010000);
    w.WriteZeros(4);
    w.WriteZeros(4);
    w.WriteZeros(4);
    w.WriteUInt32(0x40000000);
  }
}
