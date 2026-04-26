using Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Models;

namespace Tests.Integration.Sqlite;

public sealed class SqliteTestFixture
{
  private string _dbPath = null!;

  public SqliteProvider Provider { get; private set; } = null!;

  public Task SetUp()
  {
    _dbPath = Path.Combine(Path.GetTempPath(), $"vms-test-{Guid.NewGuid()}.db");
    Provider = new SqliteProvider();
    Migrate();
    Provider.InitializeProvider(_dbPath);
    return Task.CompletedTask;
  }

  public void Migrate()
  {
    Provider.MigrateDatabase(_dbPath, NullLogger.Instance).Switch(
      _ => { },
      error => Assert.Fail($"Migration failed: {error.Message}"));
  }

  public void TearDown()
  {
    foreach (var suffix in new[] { "", "-wal", "-shm" })
    {
      var path = _dbPath + suffix;
      if (File.Exists(path))
        File.Delete(path);
    }
  }

  public static Camera MakeCamera(string? address = null) => new()
  {
    Id = Guid.NewGuid(),
    Name = "Test Camera",
    Address = address ?? $"192.168.1.{Random.Shared.Next(1, 254)}",
    ProviderId = "onvif",
    CreatedAt = 1710000000000000,
    UpdatedAt = 1710000000000000
  };

  public static CameraStream MakeStream(Guid cameraId) => new()
  {
    Id = Guid.NewGuid(),
    CameraId = cameraId,
    Profile = "main",
    FormatId = "fmp4",
    Codec = "h264",
    Resolution = "1920x1080",
    Fps = 30,
    Uri = "rtsp://192.168.1.100/stream1"
  };

  public static Segment MakeSegment(Guid streamId, ulong start, ulong end, long size = 1024) => new()
  {
    Id = Guid.NewGuid(),
    StreamId = streamId,
    StartTime = start,
    EndTime = end,
    SegmentRef = $"test/{start}.mp4",
    SizeBytes = size,
    KeyframeCount = 2
  };
}
