using System.Runtime.Loader;
using Shared.Models;

namespace Server.Plugins;

public sealed class PluginEntry
{
  public required IPlugin Plugin { get; init; }
  public required PluginMetadata Metadata { get; init; }
  public required AssemblyLoadContext LoadContext { get; init; }
  public PluginState State { get; set; } = PluginState.Discovered;
  public string? ErrorMessage { get; set; }
}
