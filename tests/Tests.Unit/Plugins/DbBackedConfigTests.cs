using Server.Plugins;
using Shared.Models;

namespace Tests.Unit.Plugins;

[TestFixture]
public class DbBackedConfigTests
{
  /// <summary>
  /// SCENARIO:
  /// Config repo has a stored string value for a key
  ///
  /// ACTION:
  /// Call Get with the key
  ///
  /// EXPECTED RESULT:
  /// Returns the stored value
  /// </summary>
  [Test]
  public void Get_ExistingKey_ReturnsStoredValue()
  {
    var repo = new FakeConfigRepo();
    repo.Store["test-plugin"] = new Dictionary<string, string>
    {
      ["mykey"] = "hello"
    };
    var config = new DbBackedConfig(repo, "test-plugin");

    var result = config.Get("mykey", "default");

    Assert.That(result, Is.EqualTo("hello"));
  }

  /// <summary>
  /// SCENARIO:
  /// Config repo has no value for a key
  ///
  /// ACTION:
  /// Call Get with the key and a default value
  ///
  /// EXPECTED RESULT:
  /// Returns the default value
  /// </summary>
  [Test]
  public void Get_MissingKey_ReturnsDefault()
  {
    var repo = new FakeConfigRepo();
    var config = new DbBackedConfig(repo, "test-plugin");

    var result = config.Get("missing", "fallback");

    Assert.That(result, Is.EqualTo("fallback"));
  }

  /// <summary>
  /// SCENARIO:
  /// Config repo returns null for a key
  ///
  /// ACTION:
  /// Call Get with the key
  ///
  /// EXPECTED RESULT:
  /// Returns the default value
  /// </summary>
  [Test]
  public void Get_NullValue_ReturnsDefault()
  {
    var repo = new FakeConfigRepo();
    repo.Store["test-plugin"] = new Dictionary<string, string>
    {
      ["nullkey"] = null!
    };
    var config = new DbBackedConfig(repo, "test-plugin");

    var result = config.Get("nullkey", "fallback");

    Assert.That(result, Is.EqualTo("fallback"));
  }

  /// <summary>
  /// SCENARIO:
  /// Config repo returns an error for a key
  ///
  /// ACTION:
  /// Call Get with the key
  ///
  /// EXPECTED RESULT:
  /// Returns the default value
  /// </summary>
  [Test]
  public void Get_RepoError_ReturnsDefault()
  {
    var repo = new FakeConfigRepo { ErrorOnGet = true };
    var config = new DbBackedConfig(repo, "test-plugin");

    var result = config.Get("anykey", "safe");

    Assert.That(result, Is.EqualTo("safe"));
  }

  /// <summary>
  /// SCENARIO:
  /// A string value is set via the config
  ///
  /// ACTION:
  /// Call Set, then read back from the repo
  ///
  /// EXPECTED RESULT:
  /// The repo contains the raw string value
  /// </summary>
  [Test]
  public void Set_StoresValue()
  {
    var repo = new FakeConfigRepo();
    var config = new DbBackedConfig(repo, "test-plugin");

    config.Set("path", "/data/recordings");

    Assert.That(repo.LastSetKey, Is.EqualTo("path"));
    Assert.That(repo.LastSetValue, Is.EqualTo("/data/recordings"));
  }

  /// <summary>
  /// SCENARIO:
  /// A string value is set and then retrieved
  ///
  /// ACTION:
  /// Call Set then Get
  ///
  /// EXPECTED RESULT:
  /// Round-trips correctly
  /// </summary>
  [Test]
  public void Set_String_RoundTrips()
  {
    var repo = new FakeConfigRepo();
    var config = new DbBackedConfig(repo, "test-plugin");

    config.Set("port", "8080");

    var result = config.Get("port", "0");
    Assert.That(result, Is.EqualTo("8080"));
  }

  /// <summary>
  /// SCENARIO:
  /// Multiple keys are set and retrieved
  ///
  /// ACTION:
  /// Set two keys, then Get both back
  ///
  /// EXPECTED RESULT:
  /// Both values round-trip correctly
  /// </summary>
  [Test]
  public void Set_MultipleKeys_RoundTrip()
  {
    var repo = new FakeConfigRepo();
    var config = new DbBackedConfig(repo, "test-plugin");

    config.Set("enabled", "true");
    config.Set("name", "test");

    Assert.That(config.Get("enabled", "false"), Is.EqualTo("true"));
    Assert.That(config.Get("name", ""), Is.EqualTo("test"));
  }

  private sealed class FakeConfigRepo : IConfigRepository
  {
    public Dictionary<string, Dictionary<string, string>> Store { get; } = [];
    public bool ErrorOnGet { get; set; }
    public string? LastSetKey { get; private set; }
    public string? LastSetValue { get; private set; }

    public Task<OneOf<string?, Error>> GetAsync(string pluginId, string key, CancellationToken ct = default)
    {
      if (ErrorOnGet)
        return Task.FromResult<OneOf<string?, Error>>(
          Error.Create(0, 0, Result.InternalError, "repo error"));

      if (Store.TryGetValue(pluginId, out var dict) && dict.TryGetValue(key, out var val))
        return Task.FromResult<OneOf<string?, Error>>(val);

      return Task.FromResult<OneOf<string?, Error>>((string?)null);
    }

    public Task<OneOf<IReadOnlyDictionary<string, string>, Error>> GetAllAsync(
      string pluginId, CancellationToken ct = default) =>
      throw new NotImplementedException();

    public Task<OneOf<Success, Error>> SetAsync(
      string pluginId, string key, string value, CancellationToken ct = default)
    {
      LastSetKey = key;
      LastSetValue = value;

      if (!Store.ContainsKey(pluginId))
        Store[pluginId] = [];
      Store[pluginId][key] = value;

      return Task.FromResult<OneOf<Success, Error>>(new Success());
    }

    public Task<OneOf<Success, Error>> DeleteAsync(
      string pluginId, string key, CancellationToken ct = default) =>
      throw new NotImplementedException();
  }
}
