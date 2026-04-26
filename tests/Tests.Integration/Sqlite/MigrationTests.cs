using Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests.Integration.Sqlite;

[TestFixture]
public sealed class MigrationTests
{
  /// <summary>
  /// SCENARIO:
  /// A migrator is invoked repeatedly against the same database file
  ///
  /// ACTION:
  /// Call MigrateDatabase three times then InitializeProvider; insert and read a camera
  ///
  /// EXPECTED RESULT:
  /// Each migrate call succeeds; data ops work after init
  /// </summary>
  [Test]
  public async Task Migrate_IsIdempotent()
  {
    var path = Path.Combine(Path.GetTempPath(), $"vms-test-migrate-{Guid.NewGuid()}.db");
    try
    {
      var provider = new SqliteProvider();

      for (var i = 0; i < 3; i++)
      {
        provider.MigrateDatabase(path, NullLogger.Instance).Switch(
          _ => { },
          error => Assert.Fail($"Migrate {i + 1} failed: {error.Message}"));
      }

      provider.InitializeProvider(path);

      var camera = SqliteTestFixture.MakeCamera();
      await provider.Cameras.CreateAsync(camera);

      (await provider.Cameras.GetByIdAsync(camera.Id)).Switch(
        fetched => Assert.That(fetched, Is.Not.Null),
        error => Assert.Fail($"GetById failed: {error.Message}"));
    }
    finally
    {
      foreach (var suffix in new[] { "", "-wal", "-shm" })
      {
        var p = path + suffix;
        if (File.Exists(p))
          File.Delete(p);
      }
    }
  }
}
