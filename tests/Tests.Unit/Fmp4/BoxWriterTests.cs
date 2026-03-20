using System.Buffers.Binary;
using System.Text;
using Format.Fmp4;

namespace Tests.Unit.Fmp4;

[TestFixture]
public class BoxWriterTests
{
  /// <summary>
  /// SCENARIO:
  /// Write a simple box with a FourCC type and some data
  ///
  /// ACTION:
  /// StartBox, write data, EndBox
  ///
  /// EXPECTED RESULT:
  /// Output has 4-byte big-endian size (including header) followed by FourCC, then data
  /// </summary>
  [Test]
  public void SimpleBox_HasCorrectSizeAndType()
  {
    var w = new BoxWriter();
    w.StartBox("test");
    w.WriteUInt32(42);
    w.EndBox();

    var output = w.ToArray();
    var size = BinaryPrimitives.ReadUInt32BigEndian(output);
    var type = Encoding.ASCII.GetString(output, 4, 4);

    Assert.That(size, Is.EqualTo(12u));
    Assert.That(type, Is.EqualTo("test"));
    Assert.That(BinaryPrimitives.ReadUInt32BigEndian(output.AsSpan(8)), Is.EqualTo(42u));
  }

  /// <summary>
  /// SCENARIO:
  /// Write a full box with version and flags
  ///
  /// ACTION:
  /// StartFullBox, EndBox
  ///
  /// EXPECTED RESULT:
  /// Output has size, FourCC, 1-byte version, 3-byte flags
  /// </summary>
  [Test]
  public void FullBox_HasVersionAndFlags()
  {
    var w = new BoxWriter();
    w.StartFullBox("mvhd", 1, 0x000003);
    w.EndBox();

    var output = w.ToArray();
    var size = BinaryPrimitives.ReadUInt32BigEndian(output);

    Assert.That(size, Is.EqualTo(12u));
    Assert.That(output[8], Is.EqualTo(1));
    Assert.That(output[9], Is.EqualTo(0));
    Assert.That(output[10], Is.EqualTo(0));
    Assert.That(output[11], Is.EqualTo(3));
  }

  /// <summary>
  /// SCENARIO:
  /// Write nested boxes (parent containing a child)
  ///
  /// ACTION:
  /// StartBox parent, StartBox child, write data, EndBox child, EndBox parent
  ///
  /// EXPECTED RESULT:
  /// Parent size includes child box size, child size is correct independently
  /// </summary>
  [Test]
  public void NestedBoxes_SizesAreCorrect()
  {
    var w = new BoxWriter();
    w.StartBox("pare");
    w.StartBox("chld");
    w.WriteUInt16(0xBEEF);
    w.EndBox();
    w.EndBox();

    var output = w.ToArray();
    var parentSize = BinaryPrimitives.ReadUInt32BigEndian(output);
    var childSize = BinaryPrimitives.ReadUInt32BigEndian(output.AsSpan(8));

    Assert.That(parentSize, Is.EqualTo(18u));
    Assert.That(childSize, Is.EqualTo(10u));
  }

  /// <summary>
  /// SCENARIO:
  /// Write big-endian integers of various sizes
  ///
  /// ACTION:
  /// Write uint16, uint32, uint64 values
  ///
  /// EXPECTED RESULT:
  /// All values are big-endian encoded
  /// </summary>
  [Test]
  public void Integers_AreBigEndian()
  {
    var w = new BoxWriter();
    w.WriteUInt16(0x0102);
    w.WriteUInt32(0x03040506);
    w.WriteUInt64(0x0708090A0B0C0D0E);

    var output = w.ToArray();

    Assert.That(output[0..2], Is.EqualTo(new byte[] { 0x01, 0x02 }));
    Assert.That(output[2..6], Is.EqualTo(new byte[] { 0x03, 0x04, 0x05, 0x06 }));
    Assert.That(output[6..14], Is.EqualTo(new byte[] { 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E }));
  }

  /// <summary>
  /// SCENARIO:
  /// WriteZeros with a count larger than the internal buffer
  ///
  /// ACTION:
  /// WriteZeros(100)
  ///
  /// EXPECTED RESULT:
  /// 100 zero bytes in the output
  /// </summary>
  [Test]
  public void WriteZeros_ProducesCorrectCount()
  {
    var w = new BoxWriter();
    w.WriteZeros(100);

    var output = w.ToArray();

    Assert.That(output.Length, Is.EqualTo(100));
    Assert.That(output.All(b => b == 0), Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// Write an empty box (header only, no data)
  ///
  /// ACTION:
  /// StartBox, EndBox immediately
  ///
  /// EXPECTED RESULT:
  /// Size is 8 (just the header)
  /// </summary>
  [Test]
  public void EmptyBox_SizeIsHeaderOnly()
  {
    var w = new BoxWriter();
    w.StartBox("emty");
    w.EndBox();

    var output = w.ToArray();
    var size = BinaryPrimitives.ReadUInt32BigEndian(output);

    Assert.That(size, Is.EqualTo(8u));
    Assert.That(output.Length, Is.EqualTo(8));
  }
}
