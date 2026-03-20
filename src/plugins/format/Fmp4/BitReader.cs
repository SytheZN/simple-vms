namespace Format.Fmp4;

public ref struct BitReader
{
  private readonly ReadOnlySpan<byte> _data;
  private int _bitOffset;

  public BitReader(ReadOnlySpan<byte> data)
  {
    _data = data;
    _bitOffset = 0;
  }

  public int BitsRead => _bitOffset;
  public int BitsRemaining => _data.Length * 8 - _bitOffset;

  public uint ReadBits(int count)
  {
    uint result = 0;
    for (var i = 0; i < count; i++)
    {
      var byteIndex = _bitOffset >> 3;
      var bitIndex = 7 - (_bitOffset & 7);
      result = (result << 1) | (uint)((_data[byteIndex] >> bitIndex) & 1);
      _bitOffset++;
    }
    return result;
  }

  public bool ReadBit() => ReadBits(1) != 0;

  public uint ReadExpGolomb()
  {
    var leadingZeros = 0;
    while (!ReadBit())
      leadingZeros++;
    if (leadingZeros == 0)
      return 0;
    return (1u << leadingZeros) - 1 + ReadBits(leadingZeros);
  }

  public int ReadSignedExpGolomb()
  {
    var value = ReadExpGolomb();
    if (value == 0)
      return 0;
    var sign = (value & 1) == 1 ? 1 : -1;
    return sign * (int)((value + 1) >> 1);
  }

  public void Skip(int bits) => _bitOffset += bits;
}
