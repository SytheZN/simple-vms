using MessagePack;
using MessagePack.Resolvers;

namespace Shared.Protocol;

[GeneratedMessagePackResolver]
public partial class ProtocolResolver;

public static class ProtocolSerializer
{
  public static readonly MessagePackSerializerOptions Options =
    MessagePackSerializerOptions.Standard.WithResolver(
      CompositeResolver.Create(ProtocolResolver.Instance, StandardResolver.Instance));
}

[MessagePackObject]
public sealed class KeepaliveMessage
{
  [Key(0)] public ulong Echo { get; set; }
}

[MessagePackObject]
public sealed class ApiRequestMessage
{
  [Key(0)] public string Method { get; set; } = "";
  [Key(1)] public string Path { get; set; } = "";
  [Key(2)] public byte[]? Body { get; set; }
}

[Flags]
public enum ApiRequestFlags : ushort
{
  None = 0,
  HasBody = 1 << 0,
}

[MessagePackObject]
public sealed class ApiResponseMessage
{
  [Key(0)] public byte Result { get; set; }
  [Key(1)] public uint DebugTag { get; set; }
  [Key(2)] public string? Message { get; set; }
  [Key(3)] public byte[]? Body { get; set; }
}

[Flags]
public enum ApiResponseFlags : ushort
{
  None = 0,
  HasBody = 1 << 0,
}

[MessagePackObject]
public sealed class LiveSubscribeMessage
{
  [Key(0)] public Guid CameraId { get; set; }
  [Key(1)] public string Profile { get; set; } = "";
}

[MessagePackObject]
public sealed class PlaybackRequestMessage
{
  [Key(0)] public Guid CameraId { get; set; }
  [Key(1)] public string Profile { get; set; } = "";
  [Key(2)] public ulong From { get; set; }
  [Key(3)] public ulong? To { get; set; }
}

[Flags]
public enum PlaybackRequestFlags : ushort
{
  None = 0,
  HasEnd = 1 << 0,
}

[MessagePackObject]
public sealed class FragmentMessage
{
  [Key(0)] public ulong Timestamp { get; set; }
  [Key(1)] public byte[] Data { get; set; } = [];
}

[Flags]
public enum FragmentFlags : ushort
{
  None = 0,
  Keyframe = 1 << 0,
  Init = 1 << 1,
}

[MessagePackObject]
public sealed class EventChannelMessage
{
  [Key(0)] public Guid Id { get; set; }
  [Key(1)] public Guid CameraId { get; set; }
  [Key(2)] public string Type { get; set; } = "";
  [Key(3)] public ulong StartTime { get; set; }
  [Key(4)] public ulong? EndTime { get; set; }
  [Key(5)] public Dictionary<string, string>? Metadata { get; set; }
}

[Flags]
public enum EventChannelFlags : ushort
{
  None = 0,
  Start = 1 << 0,
  End = 1 << 1,
}

[MessagePackObject]
public sealed class StreamErrorMessage
{
  [Key(0)] public byte Result { get; set; }
  [Key(1)] public uint DebugTag { get; set; }
  [Key(2)] public string Message { get; set; } = "";
}
