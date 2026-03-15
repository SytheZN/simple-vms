namespace Shared.Models;

public sealed class CodecInfo
{
  public required string Codec { get; init; }
  public int? Profile { get; init; }
  public int? Level { get; init; }
  public ReadOnlyMemory<byte>? Sps { get; init; }
  public ReadOnlyMemory<byte>? Pps { get; init; }
  public ReadOnlyMemory<byte>? Vps { get; init; }
}
