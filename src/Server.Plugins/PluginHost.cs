using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Models;

namespace Server.Plugins;

public sealed class PluginHost
{
  private readonly List<PluginEntry> _plugins = [];
  private readonly ILogger<PluginHost> _logger;

  public IReadOnlyList<PluginEntry> Plugins => _plugins;

  public PluginHost(ILogger<PluginHost> logger)
  {
    _logger = logger;
  }

  public void Discover(string pluginsPath)
  {
    if (!Directory.Exists(pluginsPath))
      return;

    foreach (var dll in Directory.GetFiles(Path.GetFullPath(pluginsPath), "*.dll"))
    {
      try
      {
        _logger.LogDebug("Loading assembly {Path}", dll);
        var loadContext = new PluginLoadContext(dll);
        var assembly = loadContext.LoadFromAssemblyPath(dll);

        foreach (var type in FindPluginTypes(assembly))
        {
          var plugin = (IPlugin)Activator.CreateInstance(type)!;
          _plugins.Add(new PluginEntry
          {
            Plugin = plugin,
            Metadata = plugin.Metadata,
            LoadContext = loadContext,
            State = PluginState.Loaded
          });
          _logger.LogInformation("Discovered plugin: {Id} ({Name} v{Version}) from {Path}",
            plugin.Metadata.Id, plugin.Metadata.Name, plugin.Metadata.Version, dll);
        }
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Failed to load plugin from {Path}", dll);
      }
    }
  }

  public void ConfigureAll(IServiceCollection services)
  {
    foreach (var entry in _plugins.Where(p => p.State == PluginState.Loaded))
    {
      try
      {
        entry.Plugin.ConfigureServices(services);
        entry.State = PluginState.Configured;
      }
      catch (Exception ex)
      {
        entry.State = PluginState.Error;
        entry.ErrorMessage = ex.Message;
        _logger.LogError(ex, "Failed to configure plugin {Id}", entry.Metadata.Id);
      }
    }
  }

  public async Task StartAllAsync(CancellationToken ct)
  {
    foreach (var entry in _plugins.Where(p => p.State == PluginState.Configured))
    {
      await StartPluginAsync(entry, ct);
    }
  }

  public async Task StopAllAsync()
  {
    foreach (var entry in _plugins.Where(p => p.State == PluginState.Running).Reverse())
    {
      await StopPluginAsync(entry);
    }
  }

  public async Task StartPluginAsync(string pluginId, CancellationToken ct)
  {
    var entry = _plugins.FirstOrDefault(p => p.Metadata.Id == pluginId)
      ?? throw new ArgumentException($"Plugin not found: {pluginId}");
    await StartPluginAsync(entry, ct);
  }

  public async Task StopPluginAsync(string pluginId)
  {
    var entry = _plugins.FirstOrDefault(p => p.Metadata.Id == pluginId)
      ?? throw new ArgumentException($"Plugin not found: {pluginId}");
    await StopPluginAsync(entry);
  }

  private async Task StartPluginAsync(PluginEntry entry, CancellationToken ct)
  {
    try
    {
      entry.State = PluginState.Starting;
      await entry.Plugin.StartAsync(ct);
      entry.State = PluginState.Running;
      _logger.LogInformation("Started plugin {Id}", entry.Metadata.Id);
    }
    catch (Exception ex)
    {
      entry.State = PluginState.Error;
      entry.ErrorMessage = ex.Message;
      _logger.LogError(ex, "Failed to start plugin {Id}", entry.Metadata.Id);
    }
  }

  private async Task StopPluginAsync(PluginEntry entry)
  {
    try
    {
      entry.State = PluginState.Stopping;
      await entry.Plugin.StopAsync(CancellationToken.None);
      entry.State = PluginState.Stopped;
      _logger.LogInformation("Stopped plugin {Id}", entry.Metadata.Id);
    }
    catch (Exception ex)
    {
      entry.State = PluginState.Error;
      entry.ErrorMessage = ex.Message;
      _logger.LogError(ex, "Failed to stop plugin {Id}", entry.Metadata.Id);
    }
  }

  private static IEnumerable<Type> FindPluginTypes(Assembly assembly)
  {
    return assembly.GetTypes()
      .Where(t => t is { IsAbstract: false, IsInterface: false }
        && typeof(IPlugin).IsAssignableFrom(t));
  }
}
