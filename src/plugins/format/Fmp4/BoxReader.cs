using System.Buffers.Binary;
using System.Text;

namespace Format.Fmp4;

public record BoxHeader
{
  public required string Type { get; init; }
  public required long Size { get; init; }
  public required long DataOffset { get; init; }
  public long DataSize => Size - (DataOffset - (DataOffset - HeaderSize));
  public int HeaderSize => Size > uint.MaxValue ? 16 : 8;
}

public sealed class BoxReader
{
  private readonly Stream _stream;
  private readonly byte[] _buf8 = new byte[8];

  public BoxReader(Stream stream) => _stream = stream;

  public long Position
  {
    get => _stream.Position;
    set => _stream.Position = value;
  }

  public long Length => _stream.Length;

  public BoxHeader? ReadHeader()
  {
    var startPos = _stream.Position;
    if (!ReadExact(_buf8, 0, 8))
      return null;

    var size = (long)BinaryPrimitives.ReadUInt32BigEndian(_buf8);
    var type = Encoding.ASCII.GetString(_buf8, 4, 4);
    var headerSize = 8;

    if (size == 1)
    {
      if (!ReadExact(_buf8, 0, 8))
        return null;
      size = (long)BinaryPrimitives.ReadUInt64BigEndian(_buf8);
      headerSize = 16;
    }
    else if (size == 0)
    {
      size = _stream.Length - startPos;
    }

    return new BoxHeader
    {
      Type = type,
      Size = size,
      DataOffset = startPos + headerSize
    };
  }

  public (byte version, uint flags)? ReadFullBoxFields()
  {
    if (!ReadExact(_buf8, 0, 4))
      return null;
    var version = _buf8[0];
    var flags = (uint)((_buf8[1] << 16) | (_buf8[2] << 8) | _buf8[3]);
    return (version, flags);
  }

  public uint? ReadUInt32()
  {
    if (!ReadExact(_buf8, 0, 4))
      return null;
    return BinaryPrimitives.ReadUInt32BigEndian(_buf8);
  }

  public ulong? ReadUInt64()
  {
    if (!ReadExact(_buf8, 0, 8))
      return null;
    return BinaryPrimitives.ReadUInt64BigEndian(_buf8);
  }

  public byte[]? ReadBytes(int count)
  {
    var buf = new byte[count];
    return ReadExact(buf, 0, count) ? buf : null;
  }

  public void Skip(long bytes) => _stream.Position += bytes;

  private bool ReadExact(byte[] buffer, int offset, int count)
  {
    var totalRead = 0;
    while (totalRead < count)
    {
      var read = _stream.Read(buffer, offset + totalRead, count - totalRead);
      if (read == 0)
        return false;
      totalRead += read;
    }
    return true;
  }
}
