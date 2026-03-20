namespace Format.Fmp4;

public static class FtypBuilder
{
  public static byte[] Build()
  {
    var w = new BoxWriter();
    w.StartBox("ftyp");
    w.WriteFourCc("isom");
    w.WriteUInt32(512);
    w.WriteFourCc("isom");
    w.WriteFourCc("iso5");
    w.WriteFourCc("iso6");
    w.WriteFourCc("dash");
    w.WriteFourCc("mp41");
    w.EndBox();
    return w.ToArray();
  }
}
