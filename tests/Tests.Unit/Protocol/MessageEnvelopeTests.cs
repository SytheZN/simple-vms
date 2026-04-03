using Shared.Protocol;

namespace Tests.Unit.Protocol;

[TestFixture]
public class MessageEnvelopeTests
{
  /// <summary>
  /// SCENARIO:
  /// Write a stream type header and read it back
  ///
  /// ACTION:
  /// WriteStreamType then ReadStreamType on the same buffer
  ///
  /// EXPECTED RESULT:
  /// The stream type value round-trips correctly
  /// </summary>
  [TestCase((ushort)0x0100)]
  [TestCase((ushort)0x0200)]
  [TestCase((ushort)0x0300)]
  [TestCase((ushort)0x0301)]
  [TestCase((ushort)0x0400)]
  [TestCase((ushort)0x1000)]
  public void StreamTypeHeader_RoundTrips(ushort streamType)
  {
    var buf = new byte[MessageEnvelope.StreamTypeHeaderSize];
    MessageEnvelope.WriteStreamType(buf, streamType);

    var result = MessageEnvelope.ReadStreamType(buf);

    Assert.That(result, Is.EqualTo(streamType));
  }

  /// <summary>
  /// SCENARIO:
  /// Write a message envelope header with flags and length, then read it back
  ///
  /// ACTION:
  /// WriteHeader then ReadHeader on the same buffer
  ///
  /// EXPECTED RESULT:
  /// Flags and payload length round-trip correctly
  /// </summary>
  [Test]
  public void Header_RoundTrips()
  {
    var buf = new byte[MessageEnvelope.HeaderSize];
    MessageEnvelope.WriteHeader(buf, 0x0003, 1024);

    var (flags, length) = MessageEnvelope.ReadHeader(buf);

    Assert.That(flags, Is.EqualTo(0x0003));
    Assert.That(length, Is.EqualTo(1024));
  }

  /// <summary>
  /// SCENARIO:
  /// Write and read a complete envelope via stream-based async API
  ///
  /// ACTION:
  /// WriteAsync with flags and payload, then ReadAsync from the same stream
  ///
  /// EXPECTED RESULT:
  /// Flags and payload content round-trip correctly
  /// </summary>
  [Test]
  public async Task WriteAsync_ReadAsync_RoundTrips()
  {
    var payload = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
    var ms = new MemoryStream();

    await MessageEnvelopeWriter.WriteAsync(ms, 0x0001, payload, CancellationToken.None);

    ms.Position = 0;
    var (flags, readPayload) = await MessageEnvelopeReader.ReadAsync(ms, CancellationToken.None);

    Assert.That(flags, Is.EqualTo(0x0001));
    Assert.That(readPayload.ToArray(), Is.EqualTo(payload));
  }

  /// <summary>
  /// SCENARIO:
  /// Write and read an envelope with empty payload
  ///
  /// ACTION:
  /// WriteAsync with zero-length payload
  ///
  /// EXPECTED RESULT:
  /// Flags are preserved, payload is empty
  /// </summary>
  [Test]
  public async Task WriteAsync_ReadAsync_EmptyPayload()
  {
    var ms = new MemoryStream();

    await MessageEnvelopeWriter.WriteAsync(ms, 0x0042, ReadOnlyMemory<byte>.Empty, CancellationToken.None);

    ms.Position = 0;
    var (flags, payload) = await MessageEnvelopeReader.ReadAsync(ms, CancellationToken.None);

    Assert.That(flags, Is.EqualTo(0x0042));
    Assert.That(payload.Length, Is.EqualTo(0));
  }

  /// <summary>
  /// SCENARIO:
  /// Write and read a stream type header via stream-based async API
  ///
  /// ACTION:
  /// WriteStreamTypeAsync then ReadStreamTypeAsync
  ///
  /// EXPECTED RESULT:
  /// Stream type value round-trips correctly
  /// </summary>
  [Test]
  public async Task StreamTypeAsync_RoundTrips()
  {
    var ms = new MemoryStream();

    await MessageEnvelopeWriter.WriteStreamTypeAsync(ms, StreamTypes.LiveSubscribe, CancellationToken.None);

    ms.Position = 0;
    var result = await MessageEnvelopeReader.ReadStreamTypeAsync(ms, CancellationToken.None);

    Assert.That(result, Is.EqualTo(StreamTypes.LiveSubscribe));
  }

  /// <summary>
  /// SCENARIO:
  /// Attempt to write a payload exceeding the 16 MiB maximum
  ///
  /// ACTION:
  /// Call WriteAsync with a payload larger than MaxPayloadSize
  ///
  /// EXPECTED RESULT:
  /// ArgumentOutOfRangeException is thrown
  /// </summary>
  [Test]
  public void WriteAsync_RejectsOversizedPayload()
  {
    var oversized = new byte[MessageEnvelope.MaxPayloadSize + 1];
    var ms = new MemoryStream();

    Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
      await MessageEnvelopeWriter.WriteAsync(ms, 0, oversized, CancellationToken.None));
  }

  /// <summary>
  /// SCENARIO:
  /// Attempt to write a payload at exactly the 16 MiB maximum
  ///
  /// ACTION:
  /// Call WriteAsync with a payload of exactly MaxPayloadSize
  ///
  /// EXPECTED RESULT:
  /// No exception is thrown
  /// </summary>
  [Test]
  public void WriteAsync_AcceptsMaxPayload()
  {
    var maxPayload = new byte[MessageEnvelope.MaxPayloadSize];
    var ms = new MemoryStream();

    Assert.DoesNotThrowAsync(async () =>
      await MessageEnvelopeWriter.WriteAsync(ms, 0, maxPayload, CancellationToken.None));
  }

  /// <summary>
  /// SCENARIO:
  /// Read an envelope where the header claims a payload larger than 16 MiB
  ///
  /// ACTION:
  /// Manually write a header with an oversized length, then call ReadAsync
  ///
  /// EXPECTED RESULT:
  /// InvalidDataException is thrown
  /// </summary>
  [Test]
  public void ReadAsync_RejectsOversizedPayloadLength()
  {
    var ms = new MemoryStream();
    var header = new byte[MessageEnvelope.HeaderSize];
    System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(header, 0);
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
      header.AsSpan(2), (uint)(MessageEnvelope.MaxPayloadSize + 1));
    ms.Write(header);
    ms.Position = 0;

    Assert.ThrowsAsync<InvalidDataException>(async () =>
      await MessageEnvelopeReader.ReadAsync(ms, CancellationToken.None));
  }

  /// <summary>
  /// SCENARIO:
  /// Write multiple envelopes sequentially to a stream and read them back
  ///
  /// ACTION:
  /// Write 3 envelopes with different flags/payloads, then read them sequentially
  ///
  /// EXPECTED RESULT:
  /// Each envelope round-trips correctly in order
  /// </summary>
  [Test]
  public async Task MultipleEnvelopes_RoundTrip()
  {
    var ms = new MemoryStream();

    await MessageEnvelopeWriter.WriteAsync(ms, 0x0001, new byte[] { 0x01 }, CancellationToken.None);
    await MessageEnvelopeWriter.WriteAsync(ms, 0x0002, new byte[] { 0x02, 0x03 }, CancellationToken.None);
    await MessageEnvelopeWriter.WriteAsync(ms, 0x0003, ReadOnlyMemory<byte>.Empty, CancellationToken.None);

    ms.Position = 0;

    var (f1, p1) = await MessageEnvelopeReader.ReadAsync(ms, CancellationToken.None);
    Assert.That(f1, Is.EqualTo(0x0001));
    Assert.That(p1.ToArray(), Is.EqualTo(new byte[] { 0x01 }));

    var (f2, p2) = await MessageEnvelopeReader.ReadAsync(ms, CancellationToken.None);
    Assert.That(f2, Is.EqualTo(0x0002));
    Assert.That(p2.ToArray(), Is.EqualTo(new byte[] { 0x02, 0x03 }));

    var (f3, p3) = await MessageEnvelopeReader.ReadAsync(ms, CancellationToken.None);
    Assert.That(f3, Is.EqualTo(0x0003));
    Assert.That(p3.Length, Is.EqualTo(0));
  }

  /// <summary>
  /// SCENARIO:
  /// Attempt to write an oversized payload via the synchronous span-based Write
  ///
  /// ACTION:
  /// Call Write with a payload larger than MaxPayloadSize
  ///
  /// EXPECTED RESULT:
  /// ArgumentOutOfRangeException is thrown
  /// </summary>
  [Test]
  public void Write_Span_RejectsOversizedPayload()
  {
    var oversized = new byte[MessageEnvelope.MaxPayloadSize + 1];
    var buf = new byte[MessageEnvelopeWriter.GetTotalSize(oversized.Length)];

    Assert.Throws<ArgumentOutOfRangeException>(() =>
      MessageEnvelopeWriter.Write(buf, 0, oversized));
  }

  /// <summary>
  /// SCENARIO:
  /// Synchronous span-based Write and verify layout
  ///
  /// ACTION:
  /// Write envelope to a span, inspect the bytes
  ///
  /// EXPECTED RESULT:
  /// Header bytes are little-endian flags(2) + length(4), followed by payload
  /// </summary>
  [Test]
  public void Write_Span_CorrectLayout()
  {
    var payload = new byte[] { 0xAA, 0xBB };
    var buf = new byte[MessageEnvelopeWriter.GetTotalSize(payload.Length)];

    MessageEnvelopeWriter.Write(buf, 0x0005, payload);

    var (flags, length) = MessageEnvelope.ReadHeader(buf);
    Assert.That(flags, Is.EqualTo(0x0005));
    Assert.That(length, Is.EqualTo(2));
    Assert.That(buf[MessageEnvelope.HeaderSize], Is.EqualTo(0xAA));
    Assert.That(buf[MessageEnvelope.HeaderSize + 1], Is.EqualTo(0xBB));
  }
}
