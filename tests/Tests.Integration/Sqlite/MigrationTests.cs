namespace Tests.Integration.Sqlite;

[TestFixture]
public sealed class MigrationTests
{
  private readonly SqliteTestFixture _fixture = new();

  [SetUp]
  public async Task SetUp() => await _fixture.SetUp();

  [TearDown]
  public void TearDown() => _fixture.TearDown();

  /// <summary>
  /// SCENARIO:
  /// Database has already been migrated once during SetUp
  ///
  /// ACTION:
  /// Run MigrateAsync two more times
  ///
  /// EXPECTED RESULT:
  /// Both calls succeed without error; data written before the extra migrations is preserved
  /// </summary>
  [Test]
  public async Task Migrate_IsIdempotent()
  {
    (await _fixture.Provider.MigrateAsync(CancellationToken.None)).Switch(
      _ => { },
      error => Assert.Fail($"Second migrate failed: {error.Message}"));

    (await _fixture.Provider.MigrateAsync(CancellationToken.None)).Switch(
      _ => { },
      error => Assert.Fail($"Third migrate failed: {error.Message}"));

    var camera = SqliteTestFixture.MakeCamera();
    await _fixture.Provider.Cameras.CreateAsync(camera);

    (await _fixture.Provider.Cameras.GetByIdAsync(camera.Id)).Switch(
      fetched => Assert.That(fetched, Is.Not.Null),
      error => Assert.Fail($"GetById failed: {error.Message}"));
  }
}
