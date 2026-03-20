using System.Buffers.Binary;
using System.Text;

namespace Format.Fmp4;

public sealed class BoxWriter
{
  private readonly MemoryStream _stream;
  private readonly Stack<long> _boxStarts = new();

  public BoxWriter() => _stream = new MemoryStream();
  public BoxWriter(MemoryStream stream) => _stream = stream;

  public long Position => _stream.Position;
  public int Length => (int)_stream.Length;

  public void StartBox(string type)
  {
    _boxStarts.Push(_stream.Position);
    WriteUInt32(0);
    WriteFourCc(type);
  }

  public void StartFullBox(string type, byte version, uint flags)
  {
    _boxStarts.Push(_stream.Position);
    WriteUInt32(0);
    WriteFourCc(type);
    _stream.WriteByte(version);
    WriteUInt24(flags);
  }

  public void EndBox()
  {
    var start = _boxStarts.Pop();
    var size = (uint)(_stream.Position - start);
    var pos = _stream.Position;
    _stream.Position = start;
    WriteUInt32(size);
    _stream.Position = pos;
  }

  public void WriteUInt8(byte value) => _stream.WriteByte(value);

  public void WriteUInt16(ushort value)
  {
    Span<byte> buf = stackalloc byte[2];
    BinaryPrimitives.WriteUInt16BigEndian(buf, value);
    _stream.Write(buf);
  }

  public void WriteUInt24(uint value)
  {
    _stream.WriteByte((byte)(value >> 16));
    _stream.WriteByte((byte)(value >> 8));
    _stream.WriteByte((byte)value);
  }

  public void WriteUInt32(uint value)
  {
    Span<byte> buf = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32BigEndian(buf, value);
    _stream.Write(buf);
  }

  public void WriteInt32(int value)
  {
    Span<byte> buf = stackalloc byte[4];
    BinaryPrimitives.WriteInt32BigEndian(buf, value);
    _stream.Write(buf);
  }

  public void WriteUInt64(ulong value)
  {
    Span<byte> buf = stackalloc byte[8];
    BinaryPrimitives.WriteUInt64BigEndian(buf, value);
    _stream.Write(buf);
  }

  public void WriteFourCc(string fourCc)
  {
    Span<byte> buf = stackalloc byte[4];
    Encoding.ASCII.GetBytes(fourCc.AsSpan(0, 4), buf);
    _stream.Write(buf);
  }

  public void WriteBytes(ReadOnlySpan<byte> data) => _stream.Write(data);

  public void WriteZeros(int count)
  {
    Span<byte> buf = stackalloc byte[Math.Min(count, 64)];
    buf.Clear();
    var remaining = count;
    while (remaining > 0)
    {
      var chunk = Math.Min(remaining, buf.Length);
      _stream.Write(buf[..chunk]);
      remaining -= chunk;
    }
  }

  public byte[] ToArray() => _stream.ToArray();

  public ReadOnlyMemory<byte> ToMemory() =>
    _stream.TryGetBuffer(out var buffer)
      ? buffer
      : _stream.ToArray();
}
