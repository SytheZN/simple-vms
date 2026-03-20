namespace Shared.Models.Formats;

public sealed class H264Parameters
{
  public required ReadOnlyMemory<byte> Sps { get; init; }
  public required ReadOnlyMemory<byte> Pps { get; init; }
}
