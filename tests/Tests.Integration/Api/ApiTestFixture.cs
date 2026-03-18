using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Server;
using Server.Api;
using Server.Plugins;
using Server.Core;
using Shared.Models;
using Dto = Shared.Models.Dto;

namespace Tests.Integration.Api;

[SetUpFixture]
public sealed class ApiTestFixture
{
  public static WebApplication App { get; private set; } = null!;
  public static HttpClient Client { get; private set; } = null!;

  private static string _tempDir = null!;

  [OneTimeSetUp]
  public async Task Setup()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), $"vms-api-test-{Guid.NewGuid()}");
    Directory.CreateDirectory(_tempDir);

    var pluginsDir = Path.Combine(_tempDir, "plugins");
    Directory.CreateDirectory(pluginsDir);
    CopyPluginAssemblies(pluginsDir);

    var dpConfig = new DataProviderConfigJsonStore(_tempDir);
    dpConfig.SetActive("sqlite");
    dpConfig.SetProviderSettings("sqlite", new Dictionary<string, object>
    {
      ["directory"] = _tempDir,
      ["filename"] = "server.db"
    });

    var builder = WebApplication.CreateBuilder();
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
      ["data-path"] = _tempDir,
      ["http-port"] = "0",
      ["bind"] = "127.0.0.1",
      ["auto-certs"] = "true"
    });

    AppSetup.Configure(builder);

    App = builder.Build();
    await AppSetup.InitializeAsync(App);
    await App.StartAsync();

    var server = App.Services.GetRequiredService<IServer>();
    var addresses = server.Features.Get<IServerAddressesFeature>()!.Addresses;
    var address = addresses.First();

    var endpoints = App.Services.GetRequiredService<ServerEndpoints>();
    endpoints.HttpAddresses = [.. addresses];

    Client = new HttpClient { BaseAddress = new Uri(address) };
  }

  [OneTimeTearDown]
  public async Task Teardown()
  {
    Client?.Dispose();

    if (App is not null)
    {
      await App.StopAsync();
      await App.DisposeAsync();
    }

    try { Directory.Delete(_tempDir, true); } catch { }
  }

  public static async Task<string> EnrollClientAsync()
  {
    var startResponse = await Client.PostAsync("/api/v1/clients/enroll", null);
    var start = await Envelope<Dto.StartEnrollmentResponse>(startResponse);

    var enrollResponse = await Client.PostAsJsonAsync("/api/v1/enroll",
      new { token = start.Body!.Token });
    var enroll = await Envelope<Dto.EnrollResponse>(enrollResponse);
    return enroll.Body!.ClientId.ToString();
  }

  public static async Task<ResponseEnvelope> Envelope(HttpResponseMessage response)
  {
    var json = await response.Content.ReadAsStringAsync();
    return JsonSerializer.Deserialize<ResponseEnvelope>(json, ApiResponse.SerializerOptions)!;
  }

  public static async Task<ResponseEnvelope<T>> Envelope<T>(HttpResponseMessage response)
  {
    var json = await response.Content.ReadAsStringAsync();
    return JsonSerializer.Deserialize<ResponseEnvelope<T>>(json, ApiResponse.SerializerOptions)!;
  }

  private static void CopyPluginAssemblies(string targetDir)
  {
    var debugPluginsDir = Path.GetFullPath(
      Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
        "out", "debug-data", "plugins"));

    if (!Directory.Exists(debugPluginsDir))
      return;

    CopyDirectory(debugPluginsDir, targetDir);
  }

  private static void CopyDirectory(string source, string destination)
  {
    Directory.CreateDirectory(destination);

    foreach (var file in Directory.GetFiles(source))
      File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);

    foreach (var dir in Directory.GetDirectories(source))
      CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
  }
}
