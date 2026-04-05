using System.Net;
using System.Net.Http.Json;
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

  /// <summary>
  /// SCENARIO:
  /// The SQLite plugin implements IPluginSettings
  ///
  /// ACTION:
  /// OPTIONS /api/v1/plugins/sqlite/config
  ///
  /// EXPECTED RESULT:
  /// 200 with an array of setting groups containing the database group
  /// </summary>
  [Test]
  public async Task GetConfigSchema_SqliteReturnsSchema()
  {
    var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/plugins/sqlite/config");
    var response = await _client.SendAsync(request);
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

    var envelope = await ApiTestFixture.Envelope<SettingGroup[]>(response);
    Assert.That(envelope.Result, Is.EqualTo(Result.Success));
    Assert.That(envelope.Body, Is.Not.Empty);
    Assert.That(envelope.Body![0].Key, Is.EqualTo("database"));
    Assert.That(envelope.Body[0].Fields, Has.Count.EqualTo(2));
  }

  /// <summary>
  /// SCENARIO:
  /// The SQLite plugin has been initialized with config values
  ///
  /// ACTION:
  /// GET /api/v1/plugins/sqlite/config
  ///
  /// EXPECTED RESULT:
  /// 200 with a dictionary containing directory and filename keys
  /// </summary>
  [Test]
  public async Task GetConfigValues_SqliteReturnsValues()
  {
    var response = await _client.GetAsync("/api/v1/plugins/sqlite/config");
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

    var envelope = await ApiTestFixture.Envelope<Dictionary<string, string>>(response);
    Assert.That(envelope.Result, Is.EqualTo(Result.Success));
    Assert.That(envelope.Body, Does.ContainKey("directory"));
    Assert.That(envelope.Body, Does.ContainKey("filename"));
  }

  /// <summary>
  /// SCENARIO:
  /// Validate a valid directory field for the SQLite plugin
  ///
  /// ACTION:
  /// POST /api/v1/plugins/sqlite/config/validate with key=directory and a valid path
  ///
  /// EXPECTED RESULT:
  /// 200 with success result
  /// </summary>
  [Test]
  public async Task ValidateField_ValidDirectory_ReturnsSuccess()
  {
    var response = await _client.PostAsJsonAsync("/api/v1/plugins/sqlite/config/validate",
      new { key = "directory", value = Path.GetTempPath() });
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

    var envelope = await ApiTestFixture.Envelope(response);
    Assert.That(envelope.Result, Is.EqualTo(Result.Success));
  }

  /// <summary>
  /// SCENARIO:
  /// Validate an empty directory field for the SQLite plugin
  ///
  /// ACTION:
  /// POST /api/v1/plugins/sqlite/config/validate with key=directory and empty value
  ///
  /// EXPECTED RESULT:
  /// 400 with error about required field
  /// </summary>
  [Test]
  public async Task ValidateField_EmptyDirectory_ReturnsError()
  {
    var response = await _client.PostAsJsonAsync("/api/v1/plugins/sqlite/config/validate",
      new { key = "directory", value = "" });
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

    var envelope = await ApiTestFixture.Envelope(response);
    Assert.That(envelope.Result, Is.EqualTo(Result.BadRequest));
  }

  /// <summary>
  /// SCENARIO:
  /// Validate a filename with invalid characters for the SQLite plugin
  ///
  /// ACTION:
  /// POST /api/v1/plugins/sqlite/config/validate with key=filename and invalid chars
  ///
  /// EXPECTED RESULT:
  /// 400 with error about invalid characters
  /// </summary>
  [Test]
  public async Task ValidateField_InvalidFilename_ReturnsError()
  {
    var response = await _client.PostAsJsonAsync("/api/v1/plugins/sqlite/config/validate",
      new { key = "filename", value = "bad/file\0name" });
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

    var envelope = await ApiTestFixture.Envelope(response);
    Assert.That(envelope.Result, Is.EqualTo(Result.BadRequest));
  }

  /// <summary>
  /// SCENARIO:
  /// No plugin with the given ID exists
  ///
  /// ACTION:
  /// GET /api/v1/plugins/nonexistent/config
  ///
  /// EXPECTED RESULT:
  /// 404 with notFound result
  /// </summary>
  [Test]
  public async Task GetConfigValues_NotFoundPlugin_Returns404()
  {
    var response = await _client.GetAsync("/api/v1/plugins/nonexistent/config");
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
  }

  /// <summary>
  /// SCENARIO:
  /// The SQLite plugin has hasSettings=true
  ///
  /// ACTION:
  /// GET /api/v1/plugins and check the hasSettings field
  ///
  /// EXPECTED RESULT:
  /// SQLite plugin has hasSettings=true
  /// </summary>
  [Test]
  public async Task ListPlugins_SqliteHasSettingsTrue()
  {
    var response = await _client.GetAsync("/api/v1/plugins");
    var plugins = (await ApiTestFixture.Envelope<PluginListItem[]>(response)).Body!;
    var sqlite = plugins.First(p => p.Id == "sqlite");
    Assert.That(sqlite.HasSettings, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// Plugin type filter is used to list only data providers
  ///
  /// ACTION:
  /// GET /api/v1/plugins?type=data
  ///
  /// EXPECTED RESULT:
  /// Only plugins with the "data" extension point are returned
  /// </summary>
  [Test]
  public async Task ListPlugins_TypeFilter_ReturnsFiltered()
  {
    var response = await _client.GetAsync("/api/v1/plugins?type=data");
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

    var plugins = (await ApiTestFixture.Envelope<PluginListItem[]>(response)).Body!;
    Assert.That(plugins, Is.Not.Empty);
    Assert.That(plugins.All(p => p.ExtensionPoints.Contains("data")), Is.True);
  }
}
