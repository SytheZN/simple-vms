namespace Format.Fmp4;

public static class HvcCBuilder
{
  public static byte[] Build(
    ReadOnlySpan<byte> rawVps,
    ReadOnlySpan<byte> rawSps,
    ReadOnlySpan<byte> rawPps,
    H265VpsInfo vpsInfo,
    H265SpsInfo spsInfo)
  {
    var ptl = spsInfo.Ptl;
    var w = new BoxWriter();

    w.WriteUInt8(1);
    w.WriteUInt8((byte)(
      (ptl.GeneralProfileSpace << 6) |
      ((ptl.GeneralTierFlag ? 1 : 0) << 5) |
      ptl.GeneralProfileIdc));
    w.WriteUInt32(ptl.GeneralProfileCompatibilityFlags);

    var constraintHigh = (uint)(ptl.GeneralConstraintIndicatorFlags >> 16);
    var constraintLow = (ushort)(ptl.GeneralConstraintIndicatorFlags & 0xFFFF);
    w.WriteUInt32(constraintHigh);
    w.WriteUInt16(constraintLow);

    w.WriteUInt8(ptl.GeneralLevelIdc);
    w.WriteUInt16(0xF000);
    w.WriteUInt8((byte)(0xFC | (spsInfo.ChromaFormatIdc & 0x03)));
    w.WriteUInt8((byte)(0xF8 | ((spsInfo.BitDepthLuma - 8) & 0x07)));
    w.WriteUInt8((byte)(0xF8 | ((spsInfo.BitDepthChroma - 8) & 0x07)));
    w.WriteUInt16(0);
    w.WriteUInt8((byte)(
      (0 << 6) |
      ((vpsInfo.NumTemporalLayers & 0x07) << 3) |
      ((vpsInfo.TemporalIdNesting ? 1 : 0) << 2) |
      3));
    w.WriteUInt8(3);

    WriteNalArray(w, 32, rawVps);
    WriteNalArray(w, 33, rawSps);
    WriteNalArray(w, 34, rawPps);

    return w.ToArray();
  }

  private static void WriteNalArray(BoxWriter w, byte nalType, ReadOnlySpan<byte> rawNal)
  {
    w.WriteUInt8(nalType);
    w.WriteUInt16(1);
    w.WriteUInt16((ushort)rawNal.Length);
    w.WriteBytes(rawNal);
  }
}
