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

    var certManager = new CertificateManager(config);

    if (config.GetValue<bool>("auto-certs"))
    {
      if (!certManager.TryLoadCerts())
        certManager.GenerateCerts();
    }
    else
    {
      certManager.TryLoadCerts();
    }

    builder.Services.AddSingleton(certManager);
    builder.Services.AddSingleton<ICertificateService>(certManager);

    var endpoints = new ServerEndpoints { QuicPort = quicPort };
    builder.Services.AddSingleton(endpoints);

    builder.Services.AddSingleton<IEventBus, EventBus>();

    using var earlyLoggerFactory = LoggerFactory.Create(b =>
    {
      b.AddConfiguration(config.GetSection("Logging"));
      b.AddConsole();
    });

    var pluginHost = new PluginHost(earlyLoggerFactory.CreateLogger<PluginHost>());
    pluginHost.Discover(pluginsPath);
    pluginHost.ConfigureAll(builder.Services);
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
    var pluginHost = app.Services.GetRequiredService<PluginHost>();
    await pluginHost.StartAllAsync(app.Lifetime.ApplicationStopping);

    var certManager = app.Services.GetRequiredService<CertificateManager>();

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
      pluginHost.StopAllAsync().GetAwaiter().GetResult();
    });

    if (certManager.HasCerts)
    {
      await CompleteStartupAsync(app);
    }
    else
    {
      _ = PollForCertsAsync(app);
    }
  }

  internal static async Task CompleteStartupAsync(WebApplication app)
  {
    var dataProvider = app.Services.GetService<IDataProvider>();
    if (dataProvider != null)
      await dataProvider.MigrateAsync(app.Lifetime.ApplicationStopping);
  }

  private static async Task PollForCertsAsync(WebApplication app)
  {
    var certManager = app.Services.GetRequiredService<CertificateManager>();
    var ct = app.Lifetime.ApplicationStopping;

    while (!ct.IsCancellationRequested)
    {
      await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

      if (certManager.HasCerts || certManager.TryLoadCerts())
      {
        await CompleteStartupAsync(app);
        return;
      }
    }
  }
}
