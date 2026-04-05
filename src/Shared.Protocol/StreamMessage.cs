using System.Buffers.Binary;
using System.Text;

namespace Shared.Protocol;

public enum ClientMessageType : byte
{
  Live = 0x01,
  Fetch = 0x02,
}

public enum ServerMessageType : byte
{
  Init = 0x01,
  Gop = 0x02,
  Status = 0x03,
}

public enum StreamStatus : byte
{
  Ack = 0x00,
  FetchComplete = 0x01,
  Gap = 0x02,
  Error = 0x04,
  Live = 0x05,
  Recording = 0x06,
}

[Flags]
public enum GopFlags : byte
{
  None = 0,
  Begin = 1 << 0,
  End = 1 << 1,
}

public readonly record struct LiveMessage(string Profile);

public readonly record struct FetchMessage(string Profile, ulong From, ulong To);

public readonly record struct InitMessage(string Profile, ReadOnlyMemory<byte> Data);

public readonly record struct GopMessage(
  GopFlags Flags, string Profile, ulong Timestamp,
  ReadOnlyMemory<byte> Data);

public readonly record struct GapStatus(ulong From, ulong To);

public static class StreamMessageWriter
{
  public static byte[] SerializeInit(string profile, ReadOnlyMemory<byte> data)
  {
    var profileBytes = Encoding.UTF8.GetBytes(profile);
    var result = new byte[1 + 1 + profileBytes.Length + data.Length];
    result[0] = (byte)ServerMessageType.Init;
    result[1] = (byte)profileBytes.Length;
    profileBytes.CopyTo(result.AsSpan(2));
    data.Span.CopyTo(result.AsSpan(2 + profileBytes.Length));
    return result;
  }

  public static byte[] SerializeGop(
    GopFlags flags, string profile, ulong timestamp,
    ReadOnlyMemory<byte> data)
  {
    var profileBytes = Encoding.UTF8.GetBytes(profile);
    var headerLen = 1 + 1 + 1 + profileBytes.Length + 8;
    var result = new byte[headerLen + data.Length];
    result[0] = (byte)ServerMessageType.Gop;
    result[1] = (byte)flags;
    result[2] = (byte)profileBytes.Length;
    profileBytes.CopyTo(result.AsSpan(3));
    BinaryPrimitives.WriteUInt64BigEndian(result.AsSpan(3 + profileBytes.Length), timestamp);
    data.Span.CopyTo(result.AsSpan(headerLen));
    return result;
  }

  public static byte[] SerializeStatus(StreamStatus status) =>
    [(byte)ServerMessageType.Status, (byte)status];

  public static byte[] SerializeGap(ulong from, ulong to)
  {
    var result = new byte[2 + 8 + 8];
    result[0] = (byte)ServerMessageType.Status;
    result[1] = (byte)StreamStatus.Gap;
    BinaryPrimitives.WriteUInt64BigEndian(result.AsSpan(2), from);
    BinaryPrimitives.WriteUInt64BigEndian(result.AsSpan(10), to);
    return result;
  }
}

public static class StreamMessageReader
{
  public static ClientMessageType ReadType(ReadOnlySpan<byte> data) =>
    (ClientMessageType)data[0];

  public static ServerMessageType ReadServerType(ReadOnlySpan<byte> data) =>
    (ServerMessageType)data[0];

  public static InitMessage ReadInit(ReadOnlySpan<byte> data)
  {
    var profileLen = data[1];
    var profile = Encoding.UTF8.GetString(data.Slice(2, profileLen));
    var payload = data[(2 + profileLen)..].ToArray();
    return new InitMessage(profile, payload);
  }

  public static LiveMessage ReadLive(ReadOnlySpan<byte> data)
  {
    var profileLen = data[1];
    var profile = Encoding.UTF8.GetString(data.Slice(2, profileLen));
    return new LiveMessage(profile);
  }

  public static FetchMessage ReadFetch(ReadOnlySpan<byte> data)
  {
    var profileLen = data[1];
    var profile = Encoding.UTF8.GetString(data.Slice(2, profileLen));
    var from = BinaryPrimitives.ReadUInt64BigEndian(data[(2 + profileLen)..]);
    var to = BinaryPrimitives.ReadUInt64BigEndian(data[(2 + profileLen + 8)..]);
    return new FetchMessage(profile, from, to);
  }

  public static GopMessage ReadGop(ReadOnlySpan<byte> data)
  {
    var flags = (GopFlags)data[1];
    var profileLen = data[2];
    var profile = Encoding.UTF8.GetString(data.Slice(3, profileLen));
    var timestamp = BinaryPrimitives.ReadUInt64BigEndian(data[(3 + profileLen)..]);
    var payload = data[(3 + profileLen + 8)..].ToArray();
    return new GopMessage(flags, profile, timestamp, payload);
  }

  public static GapStatus ReadGap(ReadOnlySpan<byte> data)
  {
    var from = BinaryPrimitives.ReadUInt64BigEndian(data[2..]);
    var to = BinaryPrimitives.ReadUInt64BigEndian(data[10..]);
    return new GapStatus(from, to);
  }
}
