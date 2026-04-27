namespace Shared.Api;

public sealed class CameraConfigValues
{
  public Dictionary<string, Dictionary<string, string>> Camera { get; init; } = new();
  public Dictionary<string, Dictionary<string, Dictionary<string, string>>> Streams { get; init; } = new();
}
