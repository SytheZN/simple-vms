using Shared.Models;

namespace Tests.Integration.Sqlite;

[TestFixture]
public sealed class ConfigRepositoryTests
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
  /// Empty config store
  ///
  /// ACTION:
  /// Set a key for a plugin, then Get it
  ///
  /// EXPECTED RESULT:
  /// Returns the value that was set
  /// </summary>
  [Test]
  public async Task SetAndGet_RoundTrips()
  {
    await _db.Config.SetAsync("server", "server.name", "Home VMS");

    (await _db.Config.GetAsync("server", "server.name")).Switch(
      val => Assert.That(val, Is.EqualTo("Home VMS")),
      error => Assert.Fail($"Get failed: {error.Message}"));
  }

  /// <summary>
  /// SCENARIO:
  /// A config value exists
  ///
  /// ACTION:
  /// Set the same key with a new value
  ///
  /// EXPECTED RESULT:
  /// Get returns the updated value
  /// </summary>
  [Test]
  public async Task Set_OverwritesExisting()
  {
    await _db.Config.SetAsync("server", "server.name", "Home VMS");
    await _db.Config.SetAsync("server", "server.name", "Updated");

    (await _db.Config.GetAsync("server", "server.name")).Switch(
      val => Assert.That(val, Is.EqualTo("Updated")),
      error => Assert.Fail($"Get failed: {error.Message}"));
  }

  /// <summary>
  /// SCENARIO:
  /// One config value exists for a plugin
  ///
  /// ACTION:
  /// GetAll for that plugin
  ///
  /// EXPECTED RESULT:
  /// Returns a dictionary with exactly one entry
  /// </summary>
  [Test]
  public async Task GetAll_ReturnsAllForPlugin()
  {
    await _db.Config.SetAsync("server", "server.name", "Test");

    (await _db.Config.GetAllAsync("server")).Switch(
      all => Assert.That(all, Has.Count.EqualTo(1)),
      error => Assert.Fail($"GetAll failed: {error.Message}"));
  }

  /// <summary>
  /// SCENARIO:
  /// A config value exists
  ///
  /// ACTION:
  /// Delete it, then Get
  ///
  /// EXPECTED RESULT:
  /// Get returns null (missing key is not an error)
  /// </summary>
  [Test]
  public async Task Delete_RemovesValue()
  {
    await _db.Config.SetAsync("server", "server.name", "Home VMS");
    await _db.Config.DeleteAsync("server", "server.name");

    (await _db.Config.GetAsync("server", "server.name")).Switch(
      val => Assert.That(val, Is.Null),
      error => Assert.Fail($"Get failed: {error.Message}"));
  }

  /// <summary>
  /// SCENARIO:
  /// Empty config store
  ///
  /// ACTION:
  /// Get a key that was never set
  ///
  /// EXPECTED RESULT:
  /// Returns null (not an error)
  /// </summary>
  [Test]
  public async Task Get_MissingKeyReturnsNull()
  {
    (await _db.Config.GetAsync("server", "nonexistent")).Switch(
      val => Assert.That(val, Is.Null),
      error => Assert.Fail($"Get failed: {error.Message}"));
  }
}
