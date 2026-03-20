namespace Shared.Models.Formats;

public sealed class H265Parameters
{
  public required ReadOnlyMemory<byte> Vps { get; init; }
  public required ReadOnlyMemory<byte> Sps { get; init; }
  public required ReadOnlyMemory<byte> Pps { get; init; }
}
