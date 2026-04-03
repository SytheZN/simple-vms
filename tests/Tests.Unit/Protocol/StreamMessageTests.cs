using System.Buffers.Binary;
using System.Text;
using Shared.Protocol;

namespace Tests.Unit.Protocol;

[TestFixture]
public class StreamMessageWriterTests
{
  /// <summary>
  /// SCENARIO:
  /// Init message with profile "main" and 4-byte payload
  ///
  /// ACTION:
  /// Serialize via SerializeInit
  ///
  /// EXPECTED RESULT:
  /// First byte is Init type, second is profile length, then profile bytes, then payload
  /// </summary>
  [Test]
  public void SerializeInit_EncodesTypeProfileAndData()
  {
    var data = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };

    var result = StreamMessageWriter.SerializeInit("main", data);

    Assert.That(result[0], Is.EqualTo((byte)ServerMessageType.Init));
    Assert.That(result[1], Is.EqualTo(4));
    Assert.That(Encoding.UTF8.GetString(result, 2, 4), Is.EqualTo("main"));
    Assert.That(result.AsSpan(6).ToArray(), Is.EqualTo(data));
  }

  /// <summary>
  /// SCENARIO:
  /// Gop message with Begin flag, profile "sub", timestamp 12345, and payload
  ///
  /// ACTION:
  /// Serialize via SerializeGop
  ///
  /// EXPECTED RESULT:
  /// Correct type, flags, profile length, profile, big-endian timestamp, then payload
  /// </summary>
  [Test]
  public void SerializeGop_EncodesAllFields()
  {
    var data = new byte[] { 0x01, 0x02 };

    var result = StreamMessageWriter.SerializeGop(GopFlags.Begin, "sub", 12345UL, data);

    Assert.That(result[0], Is.EqualTo((byte)ServerMessageType.Gop));
    Assert.That(result[1], Is.EqualTo((byte)GopFlags.Begin));
    Assert.That(result[2], Is.EqualTo(3));
    Assert.That(Encoding.UTF8.GetString(result, 3, 3), Is.EqualTo("sub"));
    var timestamp = BinaryPrimitives.ReadUInt64BigEndian(result.AsSpan(6));
    Assert.That(timestamp, Is.EqualTo(12345UL));
    Assert.That(result.AsSpan(14).ToArray(), Is.EqualTo(data));
  }

  /// <summary>
  /// SCENARIO:
  /// Status messages for each StreamStatus value
  ///
  /// ACTION:
  /// Serialize each via SerializeStatus
  ///
  /// EXPECTED RESULT:
  /// 2-byte array: type=Status, second byte is the status value
  /// </summary>
  [TestCase(StreamStatus.Ack)]
  [TestCase(StreamStatus.FetchComplete)]
  [TestCase(StreamStatus.Error)]
  [TestCase(StreamStatus.Live)]
  [TestCase(StreamStatus.Recording)]
  public void SerializeStatus_EncodesTypeAndStatus(StreamStatus status)
  {
    var result = StreamMessageWriter.SerializeStatus(status);

    Assert.That(result.Length, Is.EqualTo(2));
    Assert.That(result[0], Is.EqualTo((byte)ServerMessageType.Status));
    Assert.That(result[1], Is.EqualTo((byte)status));
  }

  /// <summary>
  /// SCENARIO:
  /// Gap status with from=1000 to=2000
  ///
  /// ACTION:
  /// Serialize via SerializeGap
  ///
  /// EXPECTED RESULT:
  /// Type=Status, status=Gap, then two big-endian uint64 values
  /// </summary>
  [Test]
  public void SerializeGap_EncodesFromAndTo()
  {
    var result = StreamMessageWriter.SerializeGap(1000UL, 2000UL);

    Assert.That(result[0], Is.EqualTo((byte)ServerMessageType.Status));
    Assert.That(result[1], Is.EqualTo((byte)StreamStatus.Gap));
    var from = BinaryPrimitives.ReadUInt64BigEndian(result.AsSpan(2));
    var to = BinaryPrimitives.ReadUInt64BigEndian(result.AsSpan(10));
    Assert.That(from, Is.EqualTo(1000UL));
    Assert.That(to, Is.EqualTo(2000UL));
  }
}

[TestFixture]
public class StreamMessageReaderTests
{
  /// <summary>
  /// SCENARIO:
  /// Raw bytes for a Live client message with profile "main"
  ///
  /// ACTION:
  /// Read type and parse via ReadLive
  ///
  /// EXPECTED RESULT:
  /// Type is Live, profile is "main"
  /// </summary>
  [Test]
  public void ReadLive_ParsesProfile()
  {
    var profileBytes = Encoding.UTF8.GetBytes("main");
    var data = new byte[1 + 1 + profileBytes.Length];
    data[0] = (byte)ClientMessageType.Live;
    data[1] = (byte)profileBytes.Length;
    profileBytes.CopyTo(data.AsSpan(2));

    Assert.That(StreamMessageReader.ReadType(data), Is.EqualTo(ClientMessageType.Live));
    var msg = StreamMessageReader.ReadLive(data);
    Assert.That(msg.Profile, Is.EqualTo("main"));
  }

  /// <summary>
  /// SCENARIO:
  /// Raw bytes for a Fetch client message with profile "sub", from=100, to=200
  ///
  /// ACTION:
  /// Read type and parse via ReadFetch
  ///
  /// EXPECTED RESULT:
  /// Type is Fetch, profile/from/to match
  /// </summary>
  [Test]
  public void ReadFetch_ParsesProfileAndRange()
  {
    var profileBytes = Encoding.UTF8.GetBytes("sub");
    var data = new byte[1 + 1 + profileBytes.Length + 8 + 8];
    data[0] = (byte)ClientMessageType.Fetch;
    data[1] = (byte)profileBytes.Length;
    profileBytes.CopyTo(data.AsSpan(2));
    BinaryPrimitives.WriteUInt64BigEndian(data.AsSpan(2 + profileBytes.Length), 100UL);
    BinaryPrimitives.WriteUInt64BigEndian(data.AsSpan(2 + profileBytes.Length + 8), 200UL);

    Assert.That(StreamMessageReader.ReadType(data), Is.EqualTo(ClientMessageType.Fetch));
    var msg = StreamMessageReader.ReadFetch(data);
    Assert.That(msg.Profile, Is.EqualTo("sub"));
    Assert.That(msg.From, Is.EqualTo(100UL));
    Assert.That(msg.To, Is.EqualTo(200UL));
  }

  /// <summary>
  /// SCENARIO:
  /// Gop message serialized by the writer
  ///
  /// ACTION:
  /// Parse it back via ReadGop
  ///
  /// EXPECTED RESULT:
  /// Flags, profile, timestamp, and payload all round-trip correctly
  /// </summary>
  [Test]
  public void ReadGop_RoundTripsWithWriter()
  {
    var payload = new byte[] { 0xDE, 0xAD };
    var serialized = StreamMessageWriter.SerializeGop(
      GopFlags.Begin | GopFlags.End, "main", 999999UL, payload);

    var msg = StreamMessageReader.ReadGop(serialized);

    Assert.That(msg.Flags, Is.EqualTo(GopFlags.Begin | GopFlags.End));
    Assert.That(msg.Profile, Is.EqualTo("main"));
    Assert.That(msg.Timestamp, Is.EqualTo(999999UL));
    Assert.That(msg.Data.ToArray(), Is.EqualTo(payload));
  }

  /// <summary>
  /// SCENARIO:
  /// Gap message serialized by the writer
  ///
  /// ACTION:
  /// Parse it back via ReadGap
  ///
  /// EXPECTED RESULT:
  /// From and To round-trip correctly
  /// </summary>
  [Test]
  public void ReadGap_RoundTripsWithWriter()
  {
    var serialized = StreamMessageWriter.SerializeGap(5000UL, 10000UL);

    var gap = StreamMessageReader.ReadGap(serialized);

    Assert.That(gap.From, Is.EqualTo(5000UL));
    Assert.That(gap.To, Is.EqualTo(10000UL));
  }

  /// <summary>
  /// SCENARIO:
  /// Init message with empty profile and large data payload
  ///
  /// ACTION:
  /// Serialize and verify structure
  ///
  /// EXPECTED RESULT:
  /// Profile length is 0, all data bytes follow immediately
  /// </summary>
  [Test]
  public void SerializeInit_EmptyProfile_Works()
  {
    var data = new byte[256];
    for (var i = 0; i < 256; i++) data[i] = (byte)i;

    var result = StreamMessageWriter.SerializeInit("", data);

    Assert.That(result[0], Is.EqualTo((byte)ServerMessageType.Init));
    Assert.That(result[1], Is.EqualTo(0));
    Assert.That(result.AsSpan(2).ToArray(), Is.EqualTo(data));
  }
}
