namespace Shared.Api;

public sealed class PluginDto
{
  public required string Id { get; init; }
  public required string Name { get; init; }
  public string? Description { get; init; }
  public required string Version { get; init; }
  public required string Status { get; init; }
  public required string[] ExtensionPoints { get; init; }
  public required bool UserStartable { get; init; }
  public required bool HasSettings { get; init; }
  public required bool HasCameraSettings { get; init; }
  public required bool HasStreamSettings { get; init; }
}
