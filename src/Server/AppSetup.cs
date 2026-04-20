using System.Diagnostics.CodeAnalysis;
using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Server.Api;
using Server.Core;
using Server.Core.Routing;
using Server.Core.Services;
using Server.Logging;
using Server.Plugins;
using Server.Recording;
using Server.Streaming;
using Server.Tunnel;
using Shared.Models;
using Shared.Models.Events;

namespace Server;

public static class AppSetup
{
  private static StreamingService? _streamingService;
  private static RecordingManager? _recordingManager;
  private static RetentionEngine? _retentionEngine;
  private static EventManager? _eventManager;
  private static TunnelService? _tunnelService;
  private static HybridLoggerProvider? _loggerProvider;

  [RequiresUnreferencedCode("Plugin discovery loads assemblies dynamically")]
  public static void Configure(WebApplicationBuilder builder)
  {
    var config = builder.Configuration;
    var dataPath = config["data-path"]!;
    var tunnelPort = int.TryParse(config["tunnel-port"], out var tp) ? tp : 4433;
    var httpPort = int.TryParse(config["http-port"], out var hp) ? hp : 8080;

    _loggerProvider = new HybridLoggerProvider();

    var loggerFactory = LoggerFactory.Create(b =>
    {
      b.AddConfiguration(config.GetSection("Logging"));
      b.AddConsole();
      b.AddProvider(_loggerProvider);
    });

    builder.Services.AddSingleton<ILoggerFactory>(loggerFactory);

    var systemHealth = new SystemHealth();
    builder.Services.AddSingleton(systemHealth);

    var certManager = new CertificateManager(config);
    builder.Services.AddSingleton(certManager);
    builder.Services.AddSingleton<ICertificateService>(certManager);

    var endpoints = new ServerEndpoints { TunnelPort = tunnelPort, HttpPort = httpPort };
    builder.Services.AddSingleton(endpoints);

    var eventBus = new EventBus();
    builder.Services.AddSingleton<IEventBus>(eventBus);

    var dataProviderConfig = new DataProviderConfigJsonStore(dataPath);
    builder.Services.AddSingleton(dataProviderConfig);

    var environment = new ServerEnvironment(dataPath);

    var pluginHost = new PluginHost(
      loggerFactory.CreateLogger<PluginHost>(), loggerFactory,
      dataProviderConfig, eventBus, environment);
    var bundledPluginsPath = Path.Combine(AppContext.BaseDirectory, "plugins");
    var userPluginsPath = Path.Combine(dataPath, "plugins");
    pluginHost.Discover(bundledPluginsPath);
    pluginHost.Discover(userPluginsPath);
    builder.Services.AddSingleton(pluginHost);
    builder.Services.AddSingleton<IPluginHost>(pluginHost);

    var tapRegistry = new StreamTapRegistry();
    builder.Services.AddSingleton(tapRegistry);
    builder.Services.AddSingleton<IStreamTap>(tapRegistry);

    builder.Services.AddApiServices();

    if (builder.Environment.IsDevelopment())
    {
      builder.Services.AddCors(options =>
        options.AddDefaultPolicy(policy =>
          policy.SetIsOriginAllowed(_ => true)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()));
    }

    builder.WebHost.ConfigureKestrel((ctx, kestrel) =>
    {
      var bind = ctx.Configuration["bind"] ?? "0.0.0.0";
      kestrel.Listen(IPAddress.Parse(bind), endpoints.HttpPort, listen =>
      {
        listen.Protocols = HttpProtocols.Http1AndHttp2;
      });
    });
  }

  [RequiresUnreferencedCode("Plugin initialization uses dynamic type instantiation")]
  [RequiresDynamicCode("Pipeline construction uses dynamic fan-out types")]
  public static async Task InitializeAsync(WebApplication app)
  {
    if (app.Environment.IsDevelopment())
      app.UseCors();

    app.UseWebSockets();
    app.UseApiMiddleware();
    app.UseDefaultFiles();
    app.UseStaticFiles();
    app.MapApiEndpoints();
    app.MapFallbackToFile("index.html");

    var endpoints = app.Services.GetRequiredService<ServerEndpoints>();

    app.Lifetime.ApplicationStarted.Register(() =>
    {
      var server = app.Services.GetRequiredService<IServer>();
      var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
      if (addresses != null)
        endpoints.HttpAddresses = [.. addresses];
    });

    app.Lifetime.ApplicationStopping.Register(() =>
    {
      _tunnelService?.StopAsync().GetAwaiter().GetResult();
      _eventManager?.DisposeAsync().AsTask().GetAwaiter().GetResult();
      _retentionEngine?.DisposeAsync().AsTask().GetAwaiter().GetResult();
      _recordingManager?.DisposeAsync().AsTask().GetAwaiter().GetResult();
      _streamingService?.StopAsync().GetAwaiter().GetResult();

      var pluginHost = app.Services.GetRequiredService<IPluginHost>();
      pluginHost.StopAsync().GetAwaiter().GetResult();

      _loggerProvider?.Dispose();
    });

    var certManager = app.Services.GetRequiredService<CertificateManager>();
    var systemHealth = app.Services.GetRequiredService<SystemHealth>();

    if (TryInitializeCerts(app, certManager))
    {
      _loggerProvider?.EnableDataDir(app.Configuration["data-path"]!);
      systemHealth.TransitionToStarting();
      await CompleteStartupAsync(app, systemHealth);
    }
    else
    {
      var pluginHost = app.Services.GetRequiredService<IPluginHost>();
      pluginHost.Initialize(dataOnly: true);
      _ = PollForCertsAsync(app, certManager, systemHealth);
    }
  }

  [RequiresUnreferencedCode("Plugin initialization uses dynamic type instantiation")]
  [RequiresDynamicCode("Pipeline construction uses dynamic fan-out types")]
  internal static async Task CompleteStartupAsync(WebApplication app, SystemHealth systemHealth)
  {
    var pluginHost = app.Services.GetRequiredService<IPluginHost>();
    var tapRegistry = app.Services.GetRequiredService<StreamTapRegistry>();
    var eventBus = app.Services.GetRequiredService<IEventBus>();

    pluginHost.SetStreamTap(tapRegistry);

    pluginHost.Initialize();
    await pluginHost.StartAsync(app.Lifetime.ApplicationStopping);

    if (!IsDataProviderRunning(pluginHost))
    {
      systemHealth.TransitionToDegraded();
      _ = RetryDataProviderAsync(app, systemHealth);
      return;
    }

    await FinaliseStartupAsync(app, systemHealth);
  }

  [RequiresUnreferencedCode("Plugin initialization uses dynamic type instantiation")]
  [RequiresDynamicCode("Pipeline construction uses dynamic fan-out types")]
  private static async Task FinaliseStartupAsync(WebApplication app, SystemHealth systemHealth)
  {
    var pluginHost = app.Services.GetRequiredService<IPluginHost>();
    var tapRegistry = app.Services.GetRequiredService<StreamTapRegistry>();
    var statusTracker = app.Services.GetRequiredService<CameraStatusTracker>();
    var eventBus = app.Services.GetRequiredService<IEventBus>();
    var systemService = app.Services.GetRequiredService<SystemService>();

    await systemService.RecomputeMissingSettingsAsync(app.Lifetime.ApplicationStopping);

    WatchCameraStatus(eventBus, statusTracker, app.Lifetime.ApplicationStopping);

    var cameraRegistry = new CameraRegistry(pluginHost.DataProvider, statusTracker);
    pluginHost.SetCameraRegistry(cameraRegistry);

    _streamingService = new StreamingService(
      pluginHost, tapRegistry, eventBus,
      app.Services.GetRequiredService<ILoggerFactory>().CreateLogger<StreamingService>());
    await _streamingService.StartAsync(app.Lifetime.ApplicationStopping);

    var recordingAccess = new RecordingAccess(pluginHost);
    pluginHost.SetRecordingAccess(recordingAccess);

    _recordingManager = new RecordingManager(
      pluginHost, tapRegistry, eventBus,
      app.Services.GetRequiredService<ILoggerFactory>().CreateLogger<RecordingManager>());
    await _recordingManager.StartAsync(app.Lifetime.ApplicationStopping);

    _retentionEngine = new RetentionEngine(
      pluginHost,
      app.Services.GetRequiredService<ILoggerFactory>().CreateLogger<RetentionEngine>());
    _retentionEngine.Start(app.Lifetime.ApplicationStopping);

    _eventManager = new EventManager(
      pluginHost, eventBus,
      app.Services.GetRequiredService<ILoggerFactory>().CreateLogger<EventManager>());
    await _eventManager.StartAsync(app.Lifetime.ApplicationStopping);

    _tunnelService = new TunnelService(
      app.Services.GetRequiredService<ICertificateService>(),
      app.Services.GetRequiredService<ServerEndpoints>(),
      pluginHost,
      eventBus,
      app.Services.GetRequiredService<ConnectionTracker>(),
      app.Services.GetRequiredService<ApiDispatcher>(),
      tapRegistry,
      app.Services,
      app.Services.GetRequiredService<ILoggerFactory>());
    await _tunnelService.StartAsync(app.Lifetime.ApplicationStopping);

    systemHealth.TransitionToHealthy();
  }

  private static bool IsDataProviderRunning(IPluginHost pluginHost)
  {
    var running = pluginHost.Plugins.FirstOrDefault(p =>
      p.ExtensionPoints.Contains("data") && p.State == PluginState.Running);
    return running != null;
  }

  [RequiresUnreferencedCode("Plugin types are instantiated dynamically")]
  [RequiresDynamicCode("Pipeline construction uses dynamic fan-out types")]
  private static async Task RetryDataProviderAsync(WebApplication app, SystemHealth systemHealth)
  {
    var ct = app.Lifetime.ApplicationStopping;
    var pluginHost = app.Services.GetRequiredService<IPluginHost>();
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("DataProviderRetry");

    try
    {
      while (!ct.IsCancellationRequested && systemHealth.Status == "degraded")
      {
        try { await Task.Delay(TimeSpan.FromSeconds(30), ct); }
        catch (OperationCanceledException) { break; }

        logger.LogInformation("Retrying data provider start");
        try
        {
          pluginHost.ResetErrored();
          await pluginHost.StartAsync(ct);
        }
        catch (Exception ex)
        {
          logger.LogWarning(ex, "Data provider retry failed; will try again");
          continue;
        }

        if (IsDataProviderRunning(pluginHost))
        {
          logger.LogInformation("Data provider is now running; completing startup");
          systemHealth.TransitionToStarting();
          try
          {
            await FinaliseStartupAsync(app, systemHealth);
          }
          catch (Exception ex)
          {
            logger.LogError(ex, "Finalising startup failed after data provider recovery");
            systemHealth.TransitionToUnhealthy();
          }
          return;
        }
      }
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Data provider retry loop terminated unexpectedly");
      systemHealth.TransitionToUnhealthy();
    }
  }

  private static bool TryInitializeCerts(WebApplication app, CertificateManager certManager)
  {
    if (bool.TryParse(app.Configuration["auto-certs"], out var autoCerts) && autoCerts)
    {
      if (!certManager.TryLoadCerts())
        certManager.GenerateCerts();

      return true;
    }

    return certManager.TryLoadCerts();
  }

  private static void WatchCameraStatus(
    IEventBus eventBus, CameraStatusTracker statusTracker, CancellationToken ct)
  {
    _ = Task.Run(async () =>
    {
      await foreach (var evt in eventBus.SubscribeAsync<CameraStatusChanged>(ct))
        statusTracker.SetStatus(evt.CameraId, evt.Profile, evt.Status);
    }, ct);
  }

  [RequiresUnreferencedCode("Plugin initialization uses dynamic type instantiation")]
  [RequiresDynamicCode("Pipeline construction uses dynamic fan-out types")]
  private static async Task PollForCertsAsync(
    WebApplication app, CertificateManager certManager, SystemHealth systemHealth)
  {
    var ct = app.Lifetime.ApplicationStopping;

    while (!ct.IsCancellationRequested)
    {
      await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

      if (certManager.HasCerts || certManager.TryLoadCerts())
      {
        _loggerProvider?.EnableDataDir(app.Configuration["data-path"]!);
        systemHealth.TransitionToStarting();
        await CompleteStartupAsync(app, systemHealth);
        return;
      }
    }
  }
}
