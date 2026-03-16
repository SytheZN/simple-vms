using Shared.Models;

namespace Tests.Integration.Sqlite;

[TestFixture]
public sealed class EventRepositoryTests
{
  private readonly SqliteTestFixture _fixture = new();
  private IDataProvider _db = null!;
  private Guid _cameraId;

  [SetUp]
  public async Task SetUp()
  {
    await _fixture.SetUp();
    _db = _fixture.Provider;

    var camera = SqliteTestFixture.MakeCamera();
    _cameraId = camera.Id;
    await _db.Cameras.CreateAsync(camera);
  }

  [TearDown]
  public void TearDown() => _fixture.TearDown();

  /// <summary>
  /// SCENARIO:
  /// Three events exist: two motion and one tamper
  ///
  /// ACTION:
  /// Query with no filters
  ///
  /// EXPECTED RESULT:
  /// Returns all 3 events
  /// </summary>
  [Test]
  public async Task Query_NoFilters_ReturnsAll()
  {
    await SeedEvents();

    (await _db.Events.QueryAsync(null, null, 0, 5000, 100, 0)).Switch(
      all => Assert.That(all, Has.Count.EqualTo(3)),
      error => Assert.Fail($"QueryAsync failed: {error.Message}"));
  }

  /// <summary>
  /// SCENARIO:
  /// Three events exist: two motion and one tamper
  ///
  /// ACTION:
  /// Query filtered by type=motion
  ///
  /// EXPECTED RESULT:
  /// Returns only the 2 motion events
  /// </summary>
  [Test]
  public async Task Query_FilterByType_ReturnsMatching()
  {
    await SeedEvents();

    (await _db.Events.QueryAsync(_cameraId, "motion", 0, 5000, 100, 0)).Switch(
      motionOnly => Assert.That(motionOnly, Has.Count.EqualTo(2)),
      error => Assert.Fail($"QueryAsync motion failed: {error.Message}"));
  }

  /// <summary>
  /// SCENARIO:
  /// Events exist at timestamps 1000, 1500, 3000
  ///
  /// ACTION:
  /// GetByTimeRange 1000-2000
  ///
  /// EXPECTED RESULT:
  /// Returns the 2 events that fall within the range
  /// </summary>
  [Test]
  public async Task GetByTimeRange_ReturnsOverlapping()
  {
    await SeedEvents();

    (await _db.Events.GetByTimeRangeAsync(_cameraId, 1000, 2000)).Switch(
      timeRange => Assert.That(timeRange, Has.Count.EqualTo(2)),
      error => Assert.Fail($"GetByTimeRange failed: {error.Message}"));
  }

  /// <summary>
  /// SCENARIO:
  /// An event exists with metadata
  ///
  /// ACTION:
  /// GetById
  ///
  /// EXPECTED RESULT:
  /// Returns the event with its metadata intact
  /// </summary>
  [Test]
  public async Task GetById_ReturnsMetadata()
  {
    var evt = new CameraEvent
    {
      Id = Guid.NewGuid(),
      CameraId = _cameraId,
      Type = "motion",
      StartTime = 1000,
      EndTime = 2000,
      Metadata = new Dictionary<string, string> { ["zone"] = "front" }
    };
    await _db.Events.CreateAsync(evt);

    (await _db.Events.GetByIdAsync(evt.Id)).Switch(
      fetched => Assert.That(fetched.Metadata!["zone"], Is.EqualTo("front")),
      error => Assert.Fail($"GetById failed: {error.Message}"));
  }

  /// <summary>
  /// SCENARIO:
  /// No event with the given ID exists
  ///
  /// ACTION:
  /// GetById with a random GUID
  ///
  /// EXPECTED RESULT:
  /// NotFound error
  /// </summary>
  [Test]
  public async Task GetById_NotFound()
  {
    (await _db.Events.GetByIdAsync(Guid.NewGuid())).Switch(
      _ => Assert.Fail("Expected NotFound"),
      error => Assert.That(error.Result, Is.EqualTo(Result.NotFound)));
  }

  /// <summary>
  /// SCENARIO:
  /// Three events exist
  ///
  /// ACTION:
  /// Query with limit=1
  ///
  /// EXPECTED RESULT:
  /// Returns exactly 1 event
  /// </summary>
  [Test]
  public async Task Query_Limit_Respected()
  {
    await SeedEvents();

    (await _db.Events.QueryAsync(null, null, 0, 5000, 1, 0)).Switch(
      limited => Assert.That(limited, Has.Count.EqualTo(1)),
      error => Assert.Fail($"QueryAsync limited failed: {error.Message}"));
  }

  /// <summary>
  /// SCENARIO:
  /// Three events exist
  ///
  /// ACTION:
  /// Query with offset=2, limit=100
  ///
  /// EXPECTED RESULT:
  /// Returns 1 event (skipping the first 2)
  /// </summary>
  [Test]
  public async Task Query_Offset_SkipsRecords()
  {
    await SeedEvents();

    (await _db.Events.QueryAsync(null, null, 0, 5000, 100, 2)).Switch(
      offset => Assert.That(offset, Has.Count.EqualTo(1)),
      error => Assert.Fail($"QueryAsync offset failed: {error.Message}"));
  }

  private async Task SeedEvents()
  {
    await _db.Events.CreateAsync(new CameraEvent
    {
      Id = Guid.NewGuid(), CameraId = _cameraId, Type = "motion",
      StartTime = 1000, EndTime = 2000
    });
    await _db.Events.CreateAsync(new CameraEvent
    {
      Id = Guid.NewGuid(), CameraId = _cameraId, Type = "tamper",
      StartTime = 1500
    });
    await _db.Events.CreateAsync(new CameraEvent
    {
      Id = Guid.NewGuid(), CameraId = _cameraId, Type = "motion",
      StartTime = 3000
    });
  }
}
