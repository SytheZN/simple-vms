using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Shared.Models;

namespace Server.Plugins;

public sealed class PluginHost : IPluginHost
{
  private readonly List<PluginEntry> _plugins = [];
  private readonly ILogger<PluginHost> _logger;
  private readonly ILoggerFactory _loggerFactory;
  private readonly DataProviderConfigJsonStore _dataProviderConfig;
  private readonly IEventBus _eventBus;
  private readonly IServerEnvironment _environment;

  public IReadOnlyList<PluginEntry> Plugins => _plugins;

  private IDataProvider? _dataProvider;
  public IDataProvider DataProvider
  {
    get => _dataProvider ?? throw new InvalidOperationException("No data provider has been started");
    private set => _dataProvider = value;
  }

  private readonly List<ICaptureSource> _captureSources = [];
  private readonly List<IStreamFormat> _streamFormats = [];
  private readonly List<ICameraProvider> _cameraProviders = [];
  private readonly List<IEventFilter> _eventFilters = [];
  private readonly List<INotificationSink> _notificationSinks = [];
  private readonly List<IVideoAnalyzer> _videoAnalyzers = [];
  private readonly List<IStorageProvider> _storageProviders = [];
  private readonly List<IAuthProvider> _authProviders = [];
  private readonly List<IAuthzProvider> _authzProviders = [];

  public IReadOnlyList<ICaptureSource> CaptureSources => _captureSources;
  public IReadOnlyList<IStreamFormat> StreamFormats => _streamFormats;
  public IReadOnlyList<ICameraProvider> CameraProviders => _cameraProviders;
  public IReadOnlyList<IEventFilter> EventFilters => _eventFilters;
  public IReadOnlyList<INotificationSink> NotificationSinks => _notificationSinks;
  public IReadOnlyList<IVideoAnalyzer> VideoAnalyzers => _videoAnalyzers;
  public IReadOnlyList<IStorageProvider> StorageProviders => _storageProviders;
  public IReadOnlyList<IAuthProvider> AuthProviders => _authProviders;
  public IReadOnlyList<IAuthzProvider> AuthzProviders => _authzProviders;

  private IStreamTap? _streamTap;
  private ICameraRegistry? _cameraRegistry;
  private IRecordingAccess? _recordingAccess;

  public void SetStreamTap(IStreamTap streamTap) => _streamTap = streamTap;
  public void SetCameraRegistry(ICameraRegistry cameraRegistry) => _cameraRegistry = cameraRegistry;
  public void SetRecordingAccess(IRecordingAccess recordingAccess) => _recordingAccess = recordingAccess;

  public IStreamFormat? FindFormat(Type inputType) =>
    _streamFormats.FirstOrDefault(f => f.InputType == inputType);

  public PluginHost(
    ILogger<PluginHost> logger,
    ILoggerFactory loggerFactory,
    DataProviderConfigJsonStore dataProviderConfig,
    IEventBus eventBus,
    IServerEnvironment environment)
  {
    _logger = logger;
    _loggerFactory = loggerFactory;
    _dataProviderConfig = dataProviderConfig;
    _eventBus = eventBus;
    _environment = environment;
  }

  [RequiresUnreferencedCode("Plugin discovery loads assemblies dynamically")]
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

  [RequiresUnreferencedCode("Plugin initialization uses dynamic type instantiation")]
  public void Initialize(bool dataOnly = false)
  {
    ReinstantiatePlugins();

    foreach (var entry in _plugins.Where(p =>
      p.State == PluginState.Discovered
      && (!dataOnly || p.ExtensionPoints.Contains("data"))))
    {
      try
      {
        var context = BuildContext(entry);
        var result = entry.Plugin.Initialize(context);
        if (result.IsT1)
        {
          entry.State = PluginState.Error;
          entry.ErrorMessage = result.AsT1.Message;
          _logger.LogError("Failed to initialize plugin {Id}: {Message}",
            entry.Metadata.Id, result.AsT1.Message);
        }
      }
      catch (Exception ex)
      {
        entry.State = PluginState.Error;
        entry.ErrorMessage = ex.Message;
        _logger.LogError(ex, "Failed to initialize plugin {Id}", entry.Metadata.Id);
      }
    }
  }

  public async Task StartAsync(CancellationToken ct)
  {
    var dataProviderEntry = _plugins.FirstOrDefault(p =>
      p.ExtensionPoints.Contains("data")
      && p.Metadata.Id == _dataProviderConfig.ActiveProvider
      && p.State == PluginState.Discovered);

    if (dataProviderEntry != null)
    {
      await StartPluginAsync(dataProviderEntry, ct);
    }

    foreach (var entry in _plugins.Where(p =>
      p != dataProviderEntry && p.State == PluginState.Discovered))
    {
      await StartPluginAsync(entry, ct);
    }
  }

  public async Task StopAsync()
  {
    foreach (var entry in _plugins.Where(p => p.State == PluginState.Running).Reverse())
    {
      try
      {
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
  }

  [RequiresUnreferencedCode("Plugin types are instantiated dynamically")]
  private void ReinstantiatePlugins()
  {
    ClearExtensionPoints();

    foreach (var entry in _plugins)
    {
      entry.Plugin = (IPlugin)Activator.CreateInstance(entry.PluginType)!;
      entry.Metadata = entry.Plugin.Metadata;
      entry.State = PluginState.Discovered;
      entry.ErrorMessage = null;
    }
  }

  private PluginContext BuildContext(PluginEntry entry)
  {
    if (entry.ExtensionPoints.Contains("data"))
    {
      return new PluginContext
      {
        Config = new DataProviderConfig(_dataProviderConfig, entry.Metadata.Id),
        Environment = _environment,
        LoggerFactory = new PluginLoggerFactory(_loggerFactory, entry.Metadata.Id),
        EventBus = _eventBus
      };
    }

    return new PluginContext
    {
      Config = _dataProvider != null
        ? new DbBackedConfig(_dataProvider.Config, entry.Metadata.Id)
        : new InMemoryConfig(),
      Environment = _environment,
      LoggerFactory = new PluginLoggerFactory(_loggerFactory, entry.Metadata.Id),
      EventBus = _eventBus,
      DataStore = _dataProvider?.GetDataStore(entry.Metadata.Id),
      StreamTap = _streamTap,
      CameraRegistry = _cameraRegistry,
      RecordingAccess = _recordingAccess
    };
  }

  private async Task StartPluginAsync(PluginEntry entry, CancellationToken ct)
  {
    try
    {
      var result = await entry.Plugin.StartAsync(ct);
      if (result.IsT1)
      {
        entry.State = PluginState.Error;
        entry.ErrorMessage = result.AsT1.Message;
        _logger.LogError("Failed to start plugin {Id}: {Message}",
          entry.Metadata.Id, result.AsT1.Message);
        return;
      }

      entry.State = PluginState.Running;
      RegisterExtensionPoints(entry.Plugin);
      _logger.LogInformation("Started plugin {Id}", entry.Metadata.Id);
    }
    catch (Exception ex)
    {
      entry.State = PluginState.Error;
      entry.ErrorMessage = ex.Message;
      _logger.LogError(ex, "Failed to start plugin {Id}", entry.Metadata.Id);
    }
  }

  private void RegisterExtensionPoints(IPlugin plugin)
  {
    if (plugin is IDataProvider dp) DataProvider = dp;
    if (plugin is IAuthProvider auth) _authProviders.Add(auth);
    if (plugin is IAuthzProvider authz) _authzProviders.Add(authz);
    if (plugin is ICaptureSource cs) _captureSources.Add(cs);
    if (plugin is IStreamFormat sf) _streamFormats.Add(sf);
    if (plugin is ICameraProvider cp) _cameraProviders.Add(cp);
    if (plugin is IEventFilter ef) _eventFilters.Add(ef);
    if (plugin is INotificationSink ns) _notificationSinks.Add(ns);
    if (plugin is IVideoAnalyzer va) _videoAnalyzers.Add(va);
    if (plugin is IStorageProvider sp) _storageProviders.Add(sp);
  }

  private void ClearExtensionPoints()
  {
    _dataProvider = null;
    _captureSources.Clear();
    _streamFormats.Clear();
    _cameraProviders.Clear();
    _eventFilters.Clear();
    _notificationSinks.Clear();
    _videoAnalyzers.Clear();
    _storageProviders.Clear();
    _authProviders.Clear();
    _authzProviders.Clear();
  }

  [RequiresUnreferencedCode("Plugin assemblies are loaded and inspected dynamically")]
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
          PluginType = type,
          Plugin = plugin,
          Metadata = plugin.Metadata,
          LoadContext = loadContext,
          ExtensionPoints = DetectExtensionPoints(type)
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

  private static readonly Dictionary<Type, string> ExtensionPointNames = new()
  {
    [typeof(ICaptureSource)] = "capture-source",
    [typeof(IStreamFormat)] = "stream-format",
    [typeof(ICameraProvider)] = "camera",
    [typeof(IEventFilter)] = "event-filter",
    [typeof(INotificationSink)] = "notification-sink",
    [typeof(IVideoAnalyzer)] = "video-analyzer",
    [typeof(IStorageProvider)] = "storage",
    [typeof(IDataProvider)] = "data",
    [typeof(IAuthProvider)] = "auth",
    [typeof(IAuthzProvider)] = "authz"
  };

  private static string[] DetectExtensionPoints(
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type pluginType)
  {
    return pluginType.GetInterfaces()
      .Where(ExtensionPointNames.ContainsKey)
      .Select(t => ExtensionPointNames[t])
      .ToArray();
  }

  [RequiresUnreferencedCode("Plugin types are discovered dynamically")]
  private static IEnumerable<Type> FindPluginTypes(Assembly assembly)
  {
    return assembly.GetTypes()
      .Where(t => t is { IsAbstract: false, IsInterface: false }
        && typeof(IPlugin).IsAssignableFrom(t));
  }
}
