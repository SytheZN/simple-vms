using System.Buffers.Binary;
using Format.Fmp4;

namespace Tests.Unit.Fmp4;

[TestFixture]
public class BoxReaderTests
{
  /// <summary>
  /// SCENARIO:
  /// Read a standard box header
  ///
  /// ACTION:
  /// Write a box with size=16 and type="test", read it back
  ///
  /// EXPECTED RESULT:
  /// Header has correct size, type, and data offset
  /// </summary>
  [Test]
  public void ReadHeader_StandardBox()
  {
    var data = new byte[16];
    BinaryPrimitives.WriteUInt32BigEndian(data, 16);
    data[4] = (byte)'t'; data[5] = (byte)'e'; data[6] = (byte)'s'; data[7] = (byte)'t';

    var reader = new BoxReader(new MemoryStream(data));
    var header = reader.ReadHeader();

    Assert.That(header, Is.Not.Null);
    Assert.That(header!.Type, Is.EqualTo("test"));
    Assert.That(header.Size, Is.EqualTo(16));
    Assert.That(header.DataOffset, Is.EqualTo(8));
  }

  /// <summary>
  /// SCENARIO:
  /// Read header from empty stream
  ///
  /// ACTION:
  /// Read from a zero-length stream
  ///
  /// EXPECTED RESULT:
  /// Returns null
  /// </summary>
  [Test]
  public void ReadHeader_EmptyStream_ReturnsNull()
  {
    var reader = new BoxReader(new MemoryStream([]));
    var header = reader.ReadHeader();

    Assert.That(header, Is.Null);
  }

  /// <summary>
  /// SCENARIO:
  /// Read full box fields (version + flags)
  ///
  /// ACTION:
  /// Write version=1, flags=0x000003 and read back
  ///
  /// EXPECTED RESULT:
  /// Returns correct version and flags
  /// </summary>
  [Test]
  public void ReadFullBoxFields_ReturnsVersionAndFlags()
  {
    var data = new byte[] { 1, 0, 0, 3 };
    var reader = new BoxReader(new MemoryStream(data));
    var fields = reader.ReadFullBoxFields();

    Assert.That(fields, Is.Not.Null);
    Assert.That(fields!.Value.version, Is.EqualTo(1));
    Assert.That(fields.Value.flags, Is.EqualTo(3u));
  }

  /// <summary>
  /// SCENARIO:
  /// Read uint32 and uint64 values
  ///
  /// ACTION:
  /// Write known big-endian values and read them
  ///
  /// EXPECTED RESULT:
  /// Correct values returned
  /// </summary>
  [Test]
  public void ReadIntegers_CorrectValues()
  {
    var data = new byte[12];
    BinaryPrimitives.WriteUInt32BigEndian(data, 42);
    BinaryPrimitives.WriteUInt64BigEndian(data.AsSpan(4), 123456789UL);

    var reader = new BoxReader(new MemoryStream(data));

    Assert.That(reader.ReadUInt32(), Is.EqualTo(42u));
    Assert.That(reader.ReadUInt64(), Is.EqualTo(123456789UL));
  }

  /// <summary>
  /// SCENARIO:
  /// ReadBytes returns exact byte count
  ///
  /// ACTION:
  /// Write 5 bytes, read 5 bytes
  ///
  /// EXPECTED RESULT:
  /// Returns the exact bytes
  /// </summary>
  [Test]
  public void ReadBytes_ReturnsExactData()
  {
    byte[] data = [0xAA, 0xBB, 0xCC, 0xDD, 0xEE];
    var reader = new BoxReader(new MemoryStream(data));

    var result = reader.ReadBytes(5);

    Assert.That(result, Is.EqualTo(data));
  }

  /// <summary>
  /// SCENARIO:
  /// Skip advances position
  ///
  /// ACTION:
  /// Skip 4 bytes, read the remaining data
  ///
  /// EXPECTED RESULT:
  /// Position is advanced by 4
  /// </summary>
  [Test]
  public void Skip_AdvancesPosition()
  {
    byte[] data = [0x00, 0x00, 0x00, 0x00, 0xFF];
    var reader = new BoxReader(new MemoryStream(data));
    reader.Skip(4);

    Assert.That(reader.Position, Is.EqualTo(4));
    var result = reader.ReadBytes(1);
    Assert.That(result![0], Is.EqualTo(0xFF));
  }

  /// <summary>
  /// SCENARIO:
  /// Box with size=0 means "extends to end of stream"
  ///
  /// ACTION:
  /// Write a box header with size=0
  ///
  /// EXPECTED RESULT:
  /// Size equals remaining stream length
  /// </summary>
  [Test]
  public void ReadHeader_SizeZero_ExtendsToEnd()
  {
    var data = new byte[20];
    BinaryPrimitives.WriteUInt32BigEndian(data, 0);
    data[4] = (byte)'m'; data[5] = (byte)'d'; data[6] = (byte)'a'; data[7] = (byte)'t';

    var reader = new BoxReader(new MemoryStream(data));
    var header = reader.ReadHeader();

    Assert.That(header, Is.Not.Null);
    Assert.That(header!.Size, Is.EqualTo(20));
  }
}
