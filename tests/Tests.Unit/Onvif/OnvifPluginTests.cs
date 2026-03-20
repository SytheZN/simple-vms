using Cameras.Onvif;
using Shared.Models;

namespace Tests.Unit.Onvif;

[TestFixture]
public class OnvifPluginTests
{
  /// <summary>
  /// SCENARIO:
  /// OnvifProvider is initialized with a config
  ///
  /// ACTION:
  /// Call GetSchema
  ///
  /// EXPECTED RESULT:
  /// Returns credentials setting group with username and password fields
  /// </summary>
  [Test]
  public void GetSchema_ReturnsCredentialsGroup()
  {
    var plugin = CreatePlugin();

    var schema = plugin.GetSchema();

    Assert.That(schema, Has.Count.EqualTo(1));
    Assert.That(schema[0].Key, Is.EqualTo("credentials"));
    Assert.That(schema[0].Fields, Has.Count.EqualTo(2));
    Assert.That(schema[0].Fields[0].Key, Is.EqualTo("username"));
    Assert.That(schema[0].Fields[1].Key, Is.EqualTo("password"));
  }

  /// <summary>
  /// SCENARIO:
  /// OnvifProvider is initialized with default config
  ///
  /// ACTION:
  /// Call GetValues
  ///
  /// EXPECTED RESULT:
  /// Returns default username "admin" and empty password
  /// </summary>
  [Test]
  public void GetValues_ReturnsDefaults()
  {
    var plugin = CreatePlugin();

    var values = plugin.GetValues();

    Assert.That(values["username"], Is.EqualTo("admin"));
    Assert.That(values["password"], Is.EqualTo(""));
  }

  /// <summary>
  /// SCENARIO:
  /// OnvifProvider receives new credentials via ApplyValues
  ///
  /// ACTION:
  /// Apply username and password, then read values
  ///
  /// EXPECTED RESULT:
  /// GetValues returns the applied values
  /// </summary>
  [Test]
  public void ApplyValues_UpdatesConfig()
  {
    var plugin = CreatePlugin();

    var result = plugin.ApplyValues(new Dictionary<string, object>
    {
      ["username"] = "newuser",
      ["password"] = "newpass"
    });

    Assert.That(result.IsT0, Is.True);
    var values = plugin.GetValues();
    Assert.That(values["username"], Is.EqualTo("newuser"));
    Assert.That(values["password"], Is.EqualTo("newpass"));
  }

  /// <summary>
  /// SCENARIO:
  /// ValidateValue is called
  ///
  /// ACTION:
  /// Validate any field
  ///
  /// EXPECTED RESULT:
  /// Always returns success
  /// </summary>
  [Test]
  public void ValidateValue_ReturnsSuccess()
  {
    var plugin = CreatePlugin();

    var result = plugin.ValidateValue("username", "anything");

    Assert.That(result.IsT0, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// Plugin metadata
  ///
  /// ACTION:
  /// Read metadata
  ///
  /// EXPECTED RESULT:
  /// Id is "onvif", ProviderId is "onvif"
  /// </summary>
  [Test]
  public void Metadata_HasCorrectId()
  {
    var plugin = new OnvifProvider();

    Assert.That(plugin.Metadata.Id, Is.EqualTo("onvif"));
    Assert.That(plugin.ProviderId, Is.EqualTo("onvif"));
  }

  private static OnvifProvider CreatePlugin()
  {
    var plugin = new OnvifProvider();
    var config = new InMemoryConfig();
    plugin.Initialize(new PluginContext
    {
      Config = config,
      Environment = new FakeEnvironment()
    });
    return plugin;
  }

  private sealed class InMemoryConfig : IConfig
  {
    private readonly Dictionary<string, object> _store = [];

    public T Get<T>(string key, T defaultValue) =>
      _store.TryGetValue(key, out var val) && val is T typed ? typed : defaultValue;

    public void Set<T>(string key, T value) => _store[key] = value!;
  }

  private sealed class FakeEnvironment : IServerEnvironment
  {
    public string DataPath => "/tmp/test";
  }
}
