using System.Runtime.Loader;
using Shared.Models;

namespace Server.Plugins;

public sealed class PluginEntry
{
  public required Type PluginType { get; init; }
  public IPlugin Plugin { get; set; } = null!;
  public PluginMetadata Metadata { get; set; } = null!;
  public required AssemblyLoadContext LoadContext { get; init; }
  public PluginState State { get; set; } = PluginState.Discovered;
  public string? ErrorMessage { get; set; }
  public string[] ExtensionPoints { get; set; } = [];
}
