using Shared.Models;

namespace Tests.Integration.Sqlite;

[TestFixture]
public sealed class SettingsRepositoryTests
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
  /// Empty settings store
  ///
  /// ACTION:
  /// Set a key, then Get it
  ///
  /// EXPECTED RESULT:
  /// Returns the value that was set
  /// </summary>
  [Test]
  public async Task SetAndGet_RoundTrips()
  {
    await _db.Settings.SetAsync("server.name", "Home VMS");

    (await _db.Settings.GetAsync("server.name")).Switch(
      val => Assert.That(val, Is.EqualTo("Home VMS")),
      error => Assert.Fail($"Get failed: {error.Message}"));
  }

  /// <summary>
  /// SCENARIO:
  /// A setting exists
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
    await _db.Settings.SetAsync("server.name", "Home VMS");
    await _db.Settings.SetAsync("server.name", "Updated");

    (await _db.Settings.GetAsync("server.name")).Switch(
      val => Assert.That(val, Is.EqualTo("Updated")),
      error => Assert.Fail($"Get failed: {error.Message}"));
  }

  /// <summary>
  /// SCENARIO:
  /// One setting exists
  ///
  /// ACTION:
  /// GetAll
  ///
  /// EXPECTED RESULT:
  /// Returns a dictionary with exactly one entry
  /// </summary>
  [Test]
  public async Task GetAll_ReturnsAllSettings()
  {
    await _db.Settings.SetAsync("server.name", "Test");

    (await _db.Settings.GetAllAsync()).Switch(
      all => Assert.That(all, Has.Count.EqualTo(1)),
      error => Assert.Fail($"GetAll failed: {error.Message}"));
  }

  /// <summary>
  /// SCENARIO:
  /// A setting exists
  ///
  /// ACTION:
  /// Delete it, then Get
  ///
  /// EXPECTED RESULT:
  /// Get returns null (missing key is not an error)
  /// </summary>
  [Test]
  public async Task Delete_RemovesSetting()
  {
    await _db.Settings.SetAsync("server.name", "Home VMS");
    await _db.Settings.DeleteAsync("server.name");

    (await _db.Settings.GetAsync("server.name")).Switch(
      val => Assert.That(val, Is.Null),
      error => Assert.Fail($"Get failed: {error.Message}"));
  }

  /// <summary>
  /// SCENARIO:
  /// Empty settings store
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
    (await _db.Settings.GetAsync("nonexistent")).Switch(
      val => Assert.That(val, Is.Null),
      error => Assert.Fail($"Get failed: {error.Message}"));
  }
}
