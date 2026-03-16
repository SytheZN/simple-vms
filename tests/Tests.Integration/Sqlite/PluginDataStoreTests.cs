using Shared.Models;

namespace Tests.Integration.Sqlite;

[TestFixture]
public sealed class PluginDataStoreTests
{
  private readonly SqliteTestFixture _fixture = new();
  private IDataProvider _db = null!;

  [SetUp]
  public async Task SetUp()
  {
    await _fixture.SetUp();
    _db = _fixture.Provider;
  }

  [TearDown]
  public void TearDown() => _fixture.TearDown();

  /// <summary>
  /// SCENARIO:
  /// Two plugins each store data under the same key
  ///
  /// ACTION:
  /// Set "key" in both stores, then read from each
  ///
  /// EXPECTED RESULT:
  /// Each store returns its own value (namespace isolation)
  /// </summary>
  [Test]
  public async Task Isolation_PluginsSeeOnlyOwnData()
  {
    var store1 = _db.GetPluginStore("plugin-a");
    var store2 = _db.GetPluginStore("plugin-b");

    await store1.SetAsync("key", "value-a");
    await store2.SetAsync("key", "value-b");

    (await store1.GetAsync<string>("key")).Switch(
      val => Assert.That(val, Is.EqualTo("value-a")),
      error => Assert.Fail($"Get store1 failed: {error.Message}"));

    (await store2.GetAsync<string>("key")).Switch(
      val => Assert.That(val, Is.EqualTo("value-b")),
      error => Assert.Fail($"Get store2 failed: {error.Message}"));
  }

  /// <summary>
  /// SCENARIO:
  /// Plugin A has a key stored
  ///
  /// ACTION:
  /// Delete the key from plugin A's store
  ///
  /// EXPECTED RESULT:
  /// Plugin A's key is null, plugin B's key is unaffected
  /// </summary>
  [Test]
  public async Task Delete_DoesNotAffectOtherPlugins()
  {
    var store1 = _db.GetPluginStore("plugin-a");
    var store2 = _db.GetPluginStore("plugin-b");

    await store1.SetAsync("key", "value-a");
    await store2.SetAsync("key", "value-b");

    await store1.DeleteAsync("key");

    (await store1.GetAsync<string>("key")).Switch(
      val => Assert.That(val, Is.Null),
      error => Assert.Fail($"Get store1 failed: {error.Message}"));

    (await store2.GetAsync<string>("key")).Switch(
      val => Assert.That(val, Is.EqualTo("value-b")),
      error => Assert.Fail($"Get store2 failed: {error.Message}"));
  }

  /// <summary>
  /// SCENARIO:
  /// A plugin has stored 3 complex objects with a key prefix
  ///
  /// ACTION:
  /// GetAll with the prefix
  ///
  /// EXPECTED RESULT:
  /// Returns all 3 entries
  /// </summary>
  [Test]
  public async Task GetAll_FiltersByPrefix()
  {
    var store = _db.GetPluginStore("test-plugin");

    await store.SetAsync("user:1", new TestUser("Alice", 30));
    await store.SetAsync("user:2", new TestUser("Bob", 25));
    await store.SetAsync("user:3", new TestUser("Charlie", 35));

    (await store.GetAllAsync<TestUser>("user:")).Switch(
      allUsers => Assert.That(allUsers, Has.Count.EqualTo(3)),
      error => Assert.Fail($"GetAll failed: {error.Message}"));
  }

  /// <summary>
  /// SCENARIO:
  /// A plugin has stored 3 users with ages 30, 25, 35
  ///
  /// ACTION:
  /// Query for users with Age > 28
  ///
  /// EXPECTED RESULT:
  /// Returns 2 users (Alice and Charlie)
  /// </summary>
  [Test]
  public async Task Query_FiltersByPredicate()
  {
    var store = _db.GetPluginStore("query-test");

    await store.SetAsync("user:1", new TestUser("Alice", 30));
    await store.SetAsync("user:2", new TestUser("Bob", 25));
    await store.SetAsync("user:3", new TestUser("Charlie", 35));

    (await store.QueryAsync<TestUser>(u => u.Age > 28)).Switch(
      over28 => Assert.That(over28, Has.Count.EqualTo(2)),
      error => Assert.Fail($"Query failed: {error.Message}"));
  }

  /// <summary>
  /// SCENARIO:
  /// Empty plugin store
  ///
  /// ACTION:
  /// Get a key that was never set
  ///
  /// EXPECTED RESULT:
  /// Returns null/default (not an error)
  /// </summary>
  [Test]
  public async Task Get_MissingKeyReturnsNull()
  {
    var store = _db.GetPluginStore("empty-plugin");

    (await store.GetAsync<string>("nonexistent")).Switch(
      val => Assert.That(val, Is.Null),
      error => Assert.Fail($"Get failed: {error.Message}"));
  }

  private sealed record TestUser(string Name, int Age);
}
