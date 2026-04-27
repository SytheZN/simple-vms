using Shared.Models;

namespace Shared.Api;

public sealed class CameraConfigSchemaResponse
{
  public Dictionary<string, IReadOnlyList<SettingGroup>> Camera { get; init; } = new();
  public Dictionary<string, Dictionary<string, IReadOnlyList<SettingGroup>>> Streams { get; init; } = new();
}
