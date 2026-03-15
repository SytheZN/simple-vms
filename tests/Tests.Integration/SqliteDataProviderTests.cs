using Data.Sqlite;
using Shared.Models;

namespace Tests.Integration;

[TestFixture]
public class SqliteDataProviderTests
{
  private string _dbPath = null!;
  private SqliteDataProvider _provider = null!;

  [OneTimeSetUp]
  public void CleanupStaleFiles()
  {
    foreach (var file in Directory.GetFiles(Path.GetTempPath(), "vms-test-*.db*"))
      File.Delete(file);
  }

  [SetUp]
  public async Task SetUp()
  {
    _dbPath = Path.Combine(Path.GetTempPath(), $"vms-test-{Guid.NewGuid()}.db");
    _provider = new SqliteDataProvider(_dbPath);
    (await _provider.MigrateAsync(CancellationToken.None)).Switch(
      _ => { },
      error => Assert.Fail($"Migration failed: {error.Message}"));
  }

  [TearDown]
  public void TearDown()
  {
    foreach (var suffix in new[] { "", "-wal", "-shm" })
    {
      var path = _dbPath + suffix;
      if (File.Exists(path))
        File.Delete(path);
    }
  }

  [Test]
  public async Task Camera_crud()
  {
    var camera = new Camera
    {
      Id = Guid.NewGuid(),
      Name = "Front Door",
      Address = "192.168.1.100",
      ProviderId = "onvif",
      Capabilities = ["ptz", "audio"],
      Config = new Dictionary<string, string> { ["key"] = "value" },
      RetentionMode = RetentionMode.Days,
      RetentionValue = 30,
      CreatedAt = 1710000000000000,
      UpdatedAt = 1710000000000000
    };

    (await _provider.Cameras.CreateAsync(camera)).Switch(
      _ => { },
      error => Assert.Fail($"Create failed: {error.Message}"));

    (await _provider.Cameras.GetByIdAsync(camera.Id)).Switch(
      fetched =>
      {
        Assert.That(fetched.Name, Is.EqualTo("Front Door"));
        Assert.That(fetched.Capabilities, Is.EqualTo(new[] { "ptz", "audio" }));
        Assert.That(fetched.Config["key"], Is.EqualTo("value"));
        Assert.That(fetched.RetentionMode, Is.EqualTo(RetentionMode.Days));
        Assert.That(fetched.RetentionValue, Is.EqualTo(30));
      },
      error => Assert.Fail($"GetById failed: {error.Message}"));

    (await _provider.Cameras.GetByAddressAsync("192.168.1.100")).Switch(
      found => Assert.That(found.Id, Is.EqualTo(camera.Id)),
      error => Assert.Fail($"GetByAddress failed: {error.Message}"));

    camera.Name = "Back Door";
    camera.UpdatedAt = 1710000001000000;
    (await _provider.Cameras.UpdateAsync(camera)).Switch(
      _ => { },
      error => Assert.Fail($"Update failed: {error.Message}"));

    (await _provider.Cameras.GetByIdAsync(camera.Id)).Switch(
      fetched => Assert.That(fetched.Name, Is.EqualTo("Back Door")),
      error => Assert.Fail($"GetById after update failed: {error.Message}"));

    (await _provider.Cameras.GetAllAsync()).Switch(
      all => Assert.That(all, Has.Count.EqualTo(1)),
      error => Assert.Fail($"GetAll failed: {error.Message}"));

    (await _provider.Cameras.DeleteAsync(camera.Id)).Switch(
      _ => { },
      error => Assert.Fail($"Delete failed: {error.Message}"));

    (await _provider.Cameras.GetByIdAsync(camera.Id)).Switch(
      _ => Assert.Fail("Expected NotFound after delete"),
      error => Assert.That(error.Result, Is.EqualTo(Result.NotFound)));
  }

  [Test]
  public async Task Camera_delete_cascades_to_streams()
  {
    var camera = MakeCamera();
    await _provider.Cameras.CreateAsync(camera);

    var stream = MakeStream(camera.Id);
    await _provider.Streams.UpsertAsync(stream);

    await _provider.Cameras.DeleteAsync(camera.Id);

    (await _provider.Streams.GetByCameraIdAsync(camera.Id)).Switch(
      streams => Assert.That(streams, Is.Empty),
      error => Assert.Fail($"GetByCameraId failed: {error.Message}"));
  }

  [Test]
  public async Task Stream_upsert_and_query()
  {
    var camera = MakeCamera();
    await _provider.Cameras.CreateAsync(camera);

    var stream = MakeStream(camera.Id);
    await _provider.Streams.UpsertAsync(stream);

    (await _provider.Streams.GetByIdAsync(stream.Id)).Switch(
      fetched =>
      {
        Assert.That(fetched.Profile, Is.EqualTo("main"));
        Assert.That(fetched.RetentionMode, Is.EqualTo(RetentionMode.Default));
      },
      error => Assert.Fail($"GetById failed: {error.Message}"));

    stream.Codec = "h265";
    stream.RetentionMode = RetentionMode.Bytes;
    stream.RetentionValue = 1073741824;
    await _provider.Streams.UpsertAsync(stream);

    (await _provider.Streams.GetByIdAsync(stream.Id)).Switch(
      fetched =>
      {
        Assert.That(fetched.Codec, Is.EqualTo("h265"));
        Assert.That(fetched.RetentionMode, Is.EqualTo(RetentionMode.Bytes));
      },
      error => Assert.Fail($"GetById after upsert failed: {error.Message}"));

    (await _provider.Streams.GetByCameraIdAsync(camera.Id)).Switch(
      byCam => Assert.That(byCam, Has.Count.EqualTo(1)),
      error => Assert.Fail($"GetByCameraId failed: {error.Message}"));

    await _provider.Streams.DeleteAsync(stream.Id);

    (await _provider.Streams.GetByIdAsync(stream.Id)).Switch(
      _ => Assert.Fail("Expected NotFound after delete"),
      error => Assert.That(error.Result, Is.EqualTo(Result.NotFound)));
  }

  [Test]
  public async Task Segment_time_range_query()
  {
    var camera = MakeCamera();
    await _provider.Cameras.CreateAsync(camera);
    var stream = MakeStream(camera.Id);
    await _provider.Streams.UpsertAsync(stream);

    var seg1 = MakeSegment(stream.Id, 1000, 2000);
    var seg2 = MakeSegment(stream.Id, 2000, 3000);
    var seg3 = MakeSegment(stream.Id, 3000, 4000);
    await _provider.Segments.CreateAsync(seg1);
    await _provider.Segments.CreateAsync(seg2);
    await _provider.Segments.CreateAsync(seg3);

    (await _provider.Segments.GetByTimeRangeAsync(stream.Id, 1500, 2500)).Switch(
      range =>
      {
        Assert.That(range, Has.Count.EqualTo(2));
        Assert.That(range[0].Id, Is.EqualTo(seg1.Id));
        Assert.That(range[1].Id, Is.EqualTo(seg2.Id));
      },
      error => Assert.Fail($"GetByTimeRange failed: {error.Message}"));
  }

  [Test]
  public async Task Segment_oldest_and_total_size()
  {
    var camera = MakeCamera();
    await _provider.Cameras.CreateAsync(camera);
    var stream = MakeStream(camera.Id);
    await _provider.Streams.UpsertAsync(stream);

    await _provider.Segments.CreateAsync(MakeSegment(stream.Id, 3000, 4000, 500));
    await _provider.Segments.CreateAsync(MakeSegment(stream.Id, 1000, 2000, 300));
    await _provider.Segments.CreateAsync(MakeSegment(stream.Id, 2000, 3000, 200));

    (await _provider.Segments.GetOldestAsync(stream.Id, 2)).Switch(
      oldest =>
      {
        Assert.That(oldest, Has.Count.EqualTo(2));
        Assert.That(oldest[0].StartTime, Is.EqualTo((ulong)1000));
        Assert.That(oldest[1].StartTime, Is.EqualTo((ulong)2000));
      },
      error => Assert.Fail($"GetOldest failed: {error.Message}"));

    (await _provider.Segments.GetTotalSizeAsync(stream.Id)).Switch(
      total => Assert.That(total, Is.EqualTo(1000)),
      error => Assert.Fail($"GetTotalSize failed: {error.Message}"));
  }

  [Test]
  public async Task Segment_delete_batch_cascades_to_keyframes()
  {
    var camera = MakeCamera();
    await _provider.Cameras.CreateAsync(camera);
    var stream = MakeStream(camera.Id);
    await _provider.Streams.UpsertAsync(stream);
    var seg = MakeSegment(stream.Id, 1000, 2000);
    await _provider.Segments.CreateAsync(seg);

    var keyframes = new List<Keyframe>
    {
      new() { SegmentId = seg.Id, Timestamp = 1000, ByteOffset = 0 },
      new() { SegmentId = seg.Id, Timestamp = 1500, ByteOffset = 50000 }
    };
    await _provider.Keyframes.CreateBatchAsync(keyframes);

    await _provider.Segments.DeleteBatchAsync([seg.Id]);

    (await _provider.Keyframes.GetBySegmentIdAsync(seg.Id)).Switch(
      kfs => Assert.That(kfs, Is.Empty),
      error => Assert.Fail($"GetBySegmentId failed: {error.Message}"));
  }

  [Test]
  public async Task Keyframe_nearest()
  {
    var camera = MakeCamera();
    await _provider.Cameras.CreateAsync(camera);
    var stream = MakeStream(camera.Id);
    await _provider.Streams.UpsertAsync(stream);
    var seg = MakeSegment(stream.Id, 1000, 5000);
    await _provider.Segments.CreateAsync(seg);

    var keyframes = new List<Keyframe>
    {
      new() { SegmentId = seg.Id, Timestamp = 1000, ByteOffset = 0 },
      new() { SegmentId = seg.Id, Timestamp = 2000, ByteOffset = 50000 },
      new() { SegmentId = seg.Id, Timestamp = 3000, ByteOffset = 100000 },
      new() { SegmentId = seg.Id, Timestamp = 4000, ByteOffset = 150000 }
    };
    await _provider.Keyframes.CreateBatchAsync(keyframes);

    (await _provider.Keyframes.GetNearestAsync(seg.Id, 2500)).Switch(
      nearest =>
      {
        Assert.That(nearest.Timestamp, Is.EqualTo((ulong)2000));
        Assert.That(nearest.ByteOffset, Is.EqualTo(50000));
      },
      error => Assert.Fail($"GetNearest failed: {error.Message}"));

    (await _provider.Keyframes.GetNearestAsync(seg.Id, 3000)).Switch(
      exact => Assert.That(exact.Timestamp, Is.EqualTo((ulong)3000)),
      error => Assert.Fail($"GetNearest exact failed: {error.Message}"));

    (await _provider.Keyframes.GetNearestAsync(seg.Id, 500)).Switch(
      _ => Assert.Fail("Expected NotFound for timestamp before all keyframes"),
      error => Assert.That(error.Result, Is.EqualTo(Result.NotFound)));
  }

  [Test]
  public async Task Event_query_with_filters()
  {
    var camera = MakeCamera();
    await _provider.Cameras.CreateAsync(camera);

    var evt1 = new CameraEvent
    {
      Id = Guid.NewGuid(), CameraId = camera.Id, Type = "motion",
      StartTime = 1000, EndTime = 2000,
      Metadata = new Dictionary<string, string> { ["zone"] = "front" }
    };
    var evt2 = new CameraEvent
    {
      Id = Guid.NewGuid(), CameraId = camera.Id, Type = "tamper",
      StartTime = 1500
    };
    var evt3 = new CameraEvent
    {
      Id = Guid.NewGuid(), CameraId = camera.Id, Type = "motion",
      StartTime = 3000
    };
    await _provider.Events.CreateAsync(evt1);
    await _provider.Events.CreateAsync(evt2);
    await _provider.Events.CreateAsync(evt3);

    (await _provider.Events.QueryAsync(null, null, 0, 5000, 100, 0)).Switch(
      all => Assert.That(all, Has.Count.EqualTo(3)),
      error => Assert.Fail($"QueryAsync failed: {error.Message}"));

    (await _provider.Events.QueryAsync(camera.Id, "motion", 0, 5000, 100, 0)).Switch(
      motionOnly => Assert.That(motionOnly, Has.Count.EqualTo(2)),
      error => Assert.Fail($"QueryAsync motion failed: {error.Message}"));

    (await _provider.Events.GetByTimeRangeAsync(camera.Id, 1000, 2000)).Switch(
      timeRange => Assert.That(timeRange, Has.Count.EqualTo(2)),
      error => Assert.Fail($"GetByTimeRange failed: {error.Message}"));

    (await _provider.Events.GetByIdAsync(evt1.Id)).Switch(
      fetched => Assert.That(fetched.Metadata!["zone"], Is.EqualTo("front")),
      error => Assert.Fail($"GetById failed: {error.Message}"));
  }

  [Test]
  public async Task Client_crud_and_revocation()
  {
    var client = new Client
    {
      Id = Guid.NewGuid(),
      Name = "My Phone",
      CertificateSerial = "ABCD1234",
      EnrolledAt = 1710000000000000
    };

    await _provider.Clients.CreateAsync(client);

    (await _provider.Clients.GetByIdAsync(client.Id)).Switch(
      fetched => Assert.That(fetched.Name, Is.EqualTo("My Phone")),
      error => Assert.Fail($"GetById failed: {error.Message}"));

    (await _provider.Clients.GetByCertificateSerialAsync("ABCD1234")).Switch(
      bySerial => Assert.That(bySerial.Revoked, Is.False),
      error => Assert.Fail($"GetByCertificateSerial failed: {error.Message}"));

    client.Revoked = true;
    await _provider.Clients.UpdateAsync(client);

    (await _provider.Clients.GetAllAsync()).Switch(
      allActive => Assert.That(allActive, Is.Empty),
      error => Assert.Fail($"GetAll failed: {error.Message}"));

    (await _provider.Clients.GetByIdAsync(client.Id)).Switch(
      _ => Assert.Fail("Expected NotFound for revoked client"),
      error => Assert.That(error.Result, Is.EqualTo(Result.NotFound)));

    (await _provider.Clients.GetByCertificateSerialAsync("ABCD1234")).Switch(
      bySerial => Assert.That(bySerial.Revoked, Is.True),
      error => Assert.Fail($"GetByCertificateSerial revoked failed: {error.Message}"));
  }

  [Test]
  public async Task Settings_crud()
  {
    await _provider.Settings.SetAsync("server.name", "Home VMS");

    (await _provider.Settings.GetAsync("server.name")).Switch(
      val => Assert.That(val, Is.EqualTo("Home VMS")),
      error => Assert.Fail($"Get failed: {error.Message}"));

    await _provider.Settings.SetAsync("server.name", "Updated");

    (await _provider.Settings.GetAsync("server.name")).Switch(
      val => Assert.That(val, Is.EqualTo("Updated")),
      error => Assert.Fail($"Get after update failed: {error.Message}"));

    (await _provider.Settings.GetAllAsync()).Switch(
      all => Assert.That(all, Has.Count.EqualTo(1)),
      error => Assert.Fail($"GetAll failed: {error.Message}"));

    await _provider.Settings.DeleteAsync("server.name");

    (await _provider.Settings.GetAsync("server.name")).Switch(
      val => Assert.That(val, Is.Null),
      error => Assert.Fail($"Get after delete failed: {error.Message}"));
  }

  [Test]
  public async Task PluginDataStore_isolation()
  {
    var store1 = _provider.GetPluginStore("plugin-a");
    var store2 = _provider.GetPluginStore("plugin-b");

    await store1.SetAsync("key", "value-a");
    await store2.SetAsync("key", "value-b");

    (await store1.GetAsync<string>("key")).Switch(
      val => Assert.That(val, Is.EqualTo("value-a")),
      error => Assert.Fail($"Get store1 failed: {error.Message}"));

    (await store2.GetAsync<string>("key")).Switch(
      val => Assert.That(val, Is.EqualTo("value-b")),
      error => Assert.Fail($"Get store2 failed: {error.Message}"));

    await store1.DeleteAsync("key");

    (await store1.GetAsync<string>("key")).Switch(
      val => Assert.That(val, Is.Null),
      error => Assert.Fail($"Get store1 after delete failed: {error.Message}"));

    (await store2.GetAsync<string>("key")).Switch(
      val => Assert.That(val, Is.EqualTo("value-b")),
      error => Assert.Fail($"Get store2 after store1 delete failed: {error.Message}"));
  }

  [Test]
  public async Task PluginDataStore_query()
  {
    var store = _provider.GetPluginStore("test-plugin");

    await store.SetAsync("user:1", new TestUser("Alice", 30));
    await store.SetAsync("user:2", new TestUser("Bob", 25));
    await store.SetAsync("user:3", new TestUser("Charlie", 35));

    (await store.GetAllAsync<TestUser>("user:")).Switch(
      allUsers => Assert.That(allUsers, Has.Count.EqualTo(3)),
      error => Assert.Fail($"GetAll failed: {error.Message}"));

    (await store.QueryAsync<TestUser>(u => u.Age > 28)).Switch(
      over28 => Assert.That(over28, Has.Count.EqualTo(2)),
      error => Assert.Fail($"Query failed: {error.Message}"));
  }

  [Test]
  public async Task Migrate_is_idempotent()
  {
    (await _provider.MigrateAsync(CancellationToken.None)).Switch(
      _ => { },
      error => Assert.Fail($"Second migrate failed: {error.Message}"));

    (await _provider.MigrateAsync(CancellationToken.None)).Switch(
      _ => { },
      error => Assert.Fail($"Third migrate failed: {error.Message}"));

    var camera = MakeCamera();
    await _provider.Cameras.CreateAsync(camera);

    (await _provider.Cameras.GetByIdAsync(camera.Id)).Switch(
      fetched => Assert.That(fetched, Is.Not.Null),
      error => Assert.Fail($"GetById failed: {error.Message}"));
  }

  private static Camera MakeCamera() => new()
  {
    Id = Guid.NewGuid(),
    Name = "Test Camera",
    Address = "192.168.1.100",
    ProviderId = "onvif",
    CreatedAt = 1710000000000000,
    UpdatedAt = 1710000000000000
  };

  private static CameraStream MakeStream(Guid cameraId) => new()
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

  private static Segment MakeSegment(Guid streamId, ulong start, ulong end, long size = 1024) => new()
  {
    Id = Guid.NewGuid(),
    StreamId = streamId,
    StartTime = start,
    EndTime = end,
    SegmentRef = $"test/{start}.mp4",
    SizeBytes = size,
    KeyframeCount = 2
  };

  private sealed record TestUser(string Name, int Age);
}
