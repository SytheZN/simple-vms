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

    var fullPath = Path.GetFullPath(pluginsPath);

    foreach (var dir in Directory.GetDirectories(fullPath))
    {
      var dirName = Path.GetFileName(dir);
      var dll = Path.Combine(dir, $"{dirName}.dll");
      if (!File.Exists(dll))
        continue;

      LoadPlugin(dll);
    }

    foreach (var dll in Directory.GetFiles(fullPath, "*.dll"))
      LoadPlugin(dll);
  }

  private void LoadPlugin(string dll)
  {
    try
    {
      _logger.LogDebug("Loading assembly {Path}", dll);
      var loadContext = new PluginLoadContext(dll);
      var assembly = loadContext.LoadFromAssemblyPath(dll);

      foreach (var type in FindPluginTypes(assembly))
      {
        var plugin = (IPlugin)Activator.CreateInstance(type)!;

        if (_plugins.Any(p => p.Metadata.Id == plugin.Metadata.Id))
        {
          _logger.LogDebug("Skipping duplicate plugin {Id} from {Path}",
            plugin.Metadata.Id, dll);
          return;
        }

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

  private static readonly HashSet<Type> ExtensionPointTypes = new(
  [
    typeof(ICaptureSource),
    typeof(IStreamFormat),
    typeof(ICameraProvider),
    typeof(IEventFilter),
    typeof(INotificationSink),
    typeof(IVideoAnalyzer),
    typeof(IStorageProvider),
    typeof(IDataProvider),
    typeof(IAuthProvider),
    typeof(IAuthzProvider)
  ]);

  public void ConfigureAll(IServiceCollection services)
  {
    foreach (var entry in _plugins.Where(p => p.State == PluginState.Loaded))
    {
      try
      {
        var before = services.Select(d => d.ServiceType).ToHashSet();
        entry.Plugin.ConfigureServices(services);
        entry.State = PluginState.Configured;
        entry.ExtensionPoints = services
          .Select(d => d.ServiceType)
          .Where(t => !before.Contains(t) && ExtensionPointTypes.Contains(t))
          .Select(t => t.Name)
          .Distinct()
          .ToArray();
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
