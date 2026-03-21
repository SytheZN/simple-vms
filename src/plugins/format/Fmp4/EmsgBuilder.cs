using System.Text;

namespace Format.Fmp4;

public static class EmsgBuilder
{
  private static readonly byte[] SchemeUri = Encoding.ASCII.GetBytes("urn:vms:wallclock\0");
  private static readonly byte[] Value = [0];

  public static byte[] Build(ulong wallClockUs, ulong presentationTime, uint timescale)
  {
    var w = new BoxWriter();
    w.StartFullBox("emsg", 1, 0);
    w.WriteUInt32(timescale);
    w.WriteUInt64(presentationTime);
    w.WriteUInt32(0);
    w.WriteUInt32(0);
    w.WriteBytes(SchemeUri);
    w.WriteBytes(Value);
    Span<byte> tsBytes = stackalloc byte[8];
    System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(tsBytes, wallClockUs);
    w.WriteBytes(tsBytes);
    w.EndBox();
    return w.ToArray();
  }
}
