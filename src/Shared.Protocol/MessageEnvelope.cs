using System.Buffers.Binary;

namespace Shared.Protocol;

public static class MessageEnvelope
{
  public const int HeaderSize = 6;
  public const int MuxHeaderSize = 10;
  public const int MaxPayloadSize = 16 * 1024 * 1024;
  public const int StreamTypeHeaderSize = 2;
  public const ushort FlagFin = 1 << 15;
  public const ushort FlagErr = 1 << 14;
  public const ushort ControlFlagMask = FlagFin | FlagErr;
  public const ushort TypeFlagMask = unchecked((ushort)~ControlFlagMask);

  public static void WriteStreamType(Span<byte> destination, ushort streamType) =>
    BinaryPrimitives.WriteUInt16LittleEndian(destination, streamType);

  public static ushort ReadStreamType(ReadOnlySpan<byte> source) =>
    BinaryPrimitives.ReadUInt16LittleEndian(source);

  public static void WriteHeader(Span<byte> destination, ushort flags, int payloadLength)
  {
    ArgumentOutOfRangeException.ThrowIfNegative(payloadLength);
    ArgumentOutOfRangeException.ThrowIfGreaterThan(payloadLength, MaxPayloadSize);
    BinaryPrimitives.WriteUInt16LittleEndian(destination, flags);
    BinaryPrimitives.WriteUInt32LittleEndian(destination[2..], (uint)payloadLength);
  }

  public static (ushort Flags, int PayloadLength) ReadHeader(ReadOnlySpan<byte> source)
  {
    var flags = BinaryPrimitives.ReadUInt16LittleEndian(source);
    var raw = BinaryPrimitives.ReadUInt32LittleEndian(source[2..]);
    if (raw > (uint)MaxPayloadSize)
      throw new InvalidDataException(
        $"Payload size {raw} exceeds maximum {MaxPayloadSize}");
    var length = (int)raw;
    return (flags, length);
  }

  public static void WriteMuxHeader(Span<byte> destination, uint streamId, ushort flags, int payloadLength)
  {
    ArgumentOutOfRangeException.ThrowIfNegative(payloadLength);
    ArgumentOutOfRangeException.ThrowIfGreaterThan(payloadLength, MaxPayloadSize);
    BinaryPrimitives.WriteUInt32LittleEndian(destination, streamId);
    BinaryPrimitives.WriteUInt16LittleEndian(destination[4..], flags);
    BinaryPrimitives.WriteUInt32LittleEndian(destination[6..], (uint)payloadLength);
  }

  public static (uint StreamId, ushort Flags, int PayloadLength) ReadMuxHeader(ReadOnlySpan<byte> source)
  {
    var streamId = BinaryPrimitives.ReadUInt32LittleEndian(source);
    var flags = BinaryPrimitives.ReadUInt16LittleEndian(source[4..]);
    var raw = BinaryPrimitives.ReadUInt32LittleEndian(source[6..]);
    if (raw > (uint)MaxPayloadSize)
      throw new InvalidDataException(
        $"Payload size {raw} exceeds maximum {MaxPayloadSize}");
    return (streamId, flags, (int)raw);
  }
}

public static class MessageEnvelopeWriter
{
  public static async Task WriteStreamTypeAsync(Stream stream, ushort streamType, CancellationToken ct)
  {
    var buf = new byte[MessageEnvelope.StreamTypeHeaderSize];
    MessageEnvelope.WriteStreamType(buf, streamType);
    await stream.WriteAsync(buf, ct);
  }

  public static async Task WriteAsync(Stream stream, ushort flags, ReadOnlyMemory<byte> payload, CancellationToken ct)
  {
    var frame = new byte[MessageEnvelope.HeaderSize + payload.Length];
    MessageEnvelope.WriteHeader(frame, flags, payload.Length);
    payload.Span.CopyTo(frame.AsSpan(MessageEnvelope.HeaderSize));
    await stream.WriteAsync(frame, ct);
  }

  public static void Write(Span<byte> destination, ushort flags, ReadOnlySpan<byte> payload)
  {
    MessageEnvelope.WriteHeader(destination, flags, payload.Length);
    payload.CopyTo(destination[MessageEnvelope.HeaderSize..]);
  }

  public static int GetTotalSize(int payloadLength) =>
    MessageEnvelope.HeaderSize + payloadLength;
}

public static class MessageEnvelopeReader
{
  public static async Task<ushort> ReadStreamTypeAsync(Stream stream, CancellationToken ct)
  {
    var buf = new byte[MessageEnvelope.StreamTypeHeaderSize];
    await stream.ReadExactlyAsync(buf, ct);
    return MessageEnvelope.ReadStreamType(buf);
  }

  public static async Task<(ushort Flags, ReadOnlyMemory<byte> Payload)> ReadAsync(
    Stream stream, CancellationToken ct)
  {
    var header = new byte[MessageEnvelope.HeaderSize];
    await stream.ReadExactlyAsync(header, ct);
    var (flags, payloadLength) = MessageEnvelope.ReadHeader(header);

    if (payloadLength == 0)
      return (flags, ReadOnlyMemory<byte>.Empty);

    var payload = new byte[payloadLength];
    await stream.ReadExactlyAsync(payload, ct);
    return (flags, payload);
  }
}
