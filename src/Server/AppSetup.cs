using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Server.Api;
using Server.Core;
using Server.Plugins;
using Shared.Models;

namespace Server;

public static class AppSetup
{
  public static void Configure(WebApplicationBuilder builder)
  {
    var config = builder.Configuration;
    var dataPath = config["data-path"]!;
    var pluginsPath = Path.Combine(dataPath, "plugins");
    var quicPort = config.GetValue("quic-port", 443);

    var systemHealth = new SystemHealth();
    builder.Services.AddSingleton(systemHealth);

    var certManager = new CertificateManager(config);
    builder.Services.AddSingleton(certManager);
    builder.Services.AddSingleton<ICertificateService>(certManager);

    var endpoints = new ServerEndpoints { QuicPort = quicPort };
    builder.Services.AddSingleton(endpoints);

    var eventBus = new EventBus();
    builder.Services.AddSingleton<IEventBus>(eventBus);

    using var earlyLoggerFactory = LoggerFactory.Create(b =>
    {
      b.AddConfiguration(config.GetSection("Logging"));
      b.AddConsole();
    });

    var dataProviderConfig = new DataProviderConfigJsonStore(dataPath);
    builder.Services.AddSingleton(dataProviderConfig);

    var environment = new ServerEnvironment(dataPath);

    var pluginHost = new PluginHost(
      earlyLoggerFactory.CreateLogger<PluginHost>(), dataProviderConfig, eventBus, environment);
    pluginHost.Discover(pluginsPath);
    builder.Services.AddSingleton(pluginHost);

    builder.Services.AddApiServices();

    builder.WebHost.ConfigureKestrel((ctx, kestrel) =>
    {
      var httpPort = ctx.Configuration.GetValue("http-port", 8080);
      var bind = ctx.Configuration["bind"] ?? "0.0.0.0";
      kestrel.Listen(IPAddress.Parse(bind), httpPort, listen =>
      {
        listen.Protocols = HttpProtocols.Http1AndHttp2;
      });
    });
  }

  public static async Task InitializeAsync(WebApplication app)
  {
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
      var pluginHost = app.Services.GetRequiredService<PluginHost>();
      pluginHost.StopAsync().GetAwaiter().GetResult();
    });

    var certManager = app.Services.GetRequiredService<CertificateManager>();
    var systemHealth = app.Services.GetRequiredService<SystemHealth>();

    if (TryInitializeCerts(app, certManager))
    {
      systemHealth.TransitionToStarting();
      await CompleteStartupAsync(app);
      systemHealth.TransitionToHealthy();
    }
    else
    {
      var pluginHost = app.Services.GetRequiredService<PluginHost>();
      pluginHost.Initialize(dataOnly: true);
      _ = PollForCertsAsync(app, certManager, systemHealth);
    }
  }

  internal static async Task CompleteStartupAsync(WebApplication app)
  {
    var pluginHost = app.Services.GetRequiredService<PluginHost>();
    pluginHost.Initialize();
    await pluginHost.StartAsync(app.Lifetime.ApplicationStopping);
  }

  private static bool TryInitializeCerts(WebApplication app, CertificateManager certManager)
  {
    if (app.Configuration.GetValue<bool>("auto-certs"))
    {
      if (!certManager.TryLoadCerts())
        certManager.GenerateCerts();

      return true;
    }

    return certManager.TryLoadCerts();
  }

  private static async Task PollForCertsAsync(
    WebApplication app, CertificateManager certManager, SystemHealth systemHealth)
  {
    var ct = app.Lifetime.ApplicationStopping;

    while (!ct.IsCancellationRequested)
    {
      await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

      if (certManager.HasCerts || certManager.TryLoadCerts())
      {
        systemHealth.TransitionToStarting();
        await CompleteStartupAsync(app);
        systemHealth.TransitionToHealthy();
        return;
      }
    }
  }
}
