using System.Net;
using Shared.Models;
using Shared.Models.Dto;

namespace Tests.Integration.Api;

[TestFixture]
public sealed class PluginTests
{
  private HttpClient _client = null!;

  [OneTimeSetUp]
  public void Setup() => _client = ApiTestFixture.Client;

  /// <summary>
  /// SCENARIO:
  /// The SQLite data provider plugin is loaded via the real plugin host
  ///
  /// ACTION:
  /// GET /api/v1/plugins
  ///
  /// EXPECTED RESULT:
  /// 200 with an array containing the sqlite plugin with id, name, version, status, and extensionPoints
  /// </summary>
  [Test]
  public async Task ListPlugins_ContainsSqliteProvider()
  {
    var response = await _client.GetAsync("/api/v1/plugins");
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

    var plugins = (await ApiTestFixture.Envelope<PluginListItem[]>(response)).Body!;
    Assert.That(plugins, Is.Not.Empty);

    var sqlite = plugins.FirstOrDefault(p => p.Id == "sqlite");
    Assert.That(sqlite, Is.Not.Null, "SQLite plugin should appear in plugin list");
    Assert.That(sqlite!.Name, Is.Not.Null.And.Not.Empty);
    Assert.That(sqlite.Version, Is.Not.Null.And.Not.Empty);
    Assert.That(sqlite.Status, Is.Not.Null.And.Not.Empty);
    Assert.That(sqlite.ExtensionPoints, Is.Not.Empty);
  }

  /// <summary>
  /// SCENARIO:
  /// The SQLite plugin is loaded
  ///
  /// ACTION:
  /// GET /api/v1/plugins/sqlite
  ///
  /// EXPECTED RESULT:
  /// 200 with the plugin's details including IDataProvider in extensionPoints
  /// </summary>
  [Test]
  public async Task GetPlugin_SqliteReturnsDetails()
  {
    var response = await _client.GetAsync("/api/v1/plugins/sqlite");
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

    var body = (await ApiTestFixture.Envelope<PluginListItem>(response)).Body!;
    Assert.That(body.Id, Is.EqualTo("sqlite"));
    Assert.That(body.ExtensionPoints, Does.Contain("data"));
  }

  /// <summary>
  /// SCENARIO:
  /// No plugin with the given ID exists
  ///
  /// ACTION:
  /// GET /api/v1/plugins/nonexistent
  ///
  /// EXPECTED RESULT:
  /// 404 with message identifying the missing plugin
  /// </summary>
  [Test]
  public async Task GetPlugin_NotFound()
  {
    var response = await _client.GetAsync("/api/v1/plugins/nonexistent");
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

    var envelope = await ApiTestFixture.Envelope(response);
    Assert.That(envelope.Result, Is.EqualTo(Result.NotFound));
    Assert.That(envelope.Message, Does.Contain("nonexistent"));
  }

  /// <summary>
  /// SCENARIO:
  /// No plugin with the given ID exists
  ///
  /// ACTION:
  /// POST /api/v1/plugins/nonexistent/start
  ///
  /// EXPECTED RESULT:
  /// 404 with notFound result
  /// </summary>
  [Test]
  public async Task StartPlugin_NotFound()
  {
    var response = await _client.PostAsync("/api/v1/plugins/nonexistent/start", null);
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

    var envelope = await ApiTestFixture.Envelope(response);
    Assert.That(envelope.Result, Is.EqualTo(Result.NotFound));
  }

  /// <summary>
  /// SCENARIO:
  /// No plugin with the given ID exists
  ///
  /// ACTION:
  /// POST /api/v1/plugins/nonexistent/stop
  ///
  /// EXPECTED RESULT:
  /// 404 with notFound result
  /// </summary>
  [Test]
  public async Task StopPlugin_NotFound()
  {
    var response = await _client.PostAsync("/api/v1/plugins/nonexistent/stop", null);
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

    var envelope = await ApiTestFixture.Envelope(response);
    Assert.That(envelope.Result, Is.EqualTo(Result.NotFound));
  }
}
