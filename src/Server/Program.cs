using Server;
using Server.Plugins;
using Shared.Models;

var builder = WebApplication.CreateBuilder(args);

var config = builder.Configuration;
var options = new ServerOptions
{
  DataPath = config["data-path"] ?? "./data",
  QuicPort = int.TryParse(config["quic-port"], out var qp) ? qp : 443,
  HttpPort = int.TryParse(config["http-port"], out var hp) ? hp : 8080,
  Bind = config["bind"] ?? "0.0.0.0"
};
builder.Services.AddSingleton(options);

var certManager = new CertificateManager(options);
certManager.Initialize();
builder.Services.AddSingleton(certManager);

builder.Services.AddSingleton<IEventBus, EventBus>();

using var earlyLoggerFactory = LoggerFactory.Create(b =>
{
  b.AddConfiguration(config.GetSection("Logging"));
  b.AddConsole();
});

var pluginHost = new PluginHost(earlyLoggerFactory.CreateLogger<PluginHost>());
pluginHost.Discover(options.PluginsPath);
pluginHost.ConfigureAll(builder.Services);
builder.Services.AddSingleton(pluginHost);

builder.WebHost.UseUrls($"http://{options.Bind}:{options.HttpPort}");

var app = builder.Build();

await pluginHost.StartAllAsync(app.Lifetime.ApplicationStopping);

app.Lifetime.ApplicationStopping.Register(() =>
{
  pluginHost.StopAllAsync().GetAwaiter().GetResult();
});

app.Run();
