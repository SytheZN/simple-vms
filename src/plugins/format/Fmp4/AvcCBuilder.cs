namespace Format.Fmp4;

public static class AvcCBuilder
{
  public static byte[] Build(ReadOnlySpan<byte> rawSps, ReadOnlySpan<byte> rawPps, H264SpsInfo spsInfo)
  {
    var w = new BoxWriter();

    w.WriteUInt8(1);
    w.WriteUInt8(spsInfo.ProfileIdc);
    w.WriteUInt8(spsInfo.ProfileCompatibility);
    w.WriteUInt8(spsInfo.LevelIdc);
    w.WriteUInt8(0xFF);
    w.WriteUInt8(0xE1);
    w.WriteUInt16((ushort)rawSps.Length);
    w.WriteBytes(rawSps);
    w.WriteUInt8(1);
    w.WriteUInt16((ushort)rawPps.Length);
    w.WriteBytes(rawPps);

    if (spsInfo.ProfileIdc is 100 or 110 or 122 or 244)
    {
      w.WriteUInt8((byte)(0xFC | (spsInfo.ChromaFormatIdc & 0x03)));
      w.WriteUInt8((byte)(0xF8 | ((spsInfo.BitDepthLuma - 8) & 0x07)));
      w.WriteUInt8((byte)(0xF8 | ((spsInfo.BitDepthChroma - 8) & 0x07)));
      w.WriteUInt8(0);
    }

    return w.ToArray();
  }
}
