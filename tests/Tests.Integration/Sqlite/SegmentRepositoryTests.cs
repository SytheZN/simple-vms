using Shared.Models;

namespace Tests.Integration.Sqlite;

[TestFixture]
public sealed class SegmentRepositoryTests
{
  private readonly SqliteTestFixture _fixture = new();
  private IDataProvider _db = null!;
  private Guid _streamId;

  [SetUp]
  public async Task SetUp()
  {
    await _fixture.SetUp();
    _db = _fixture.Provider;

    var camera = SqliteTestFixture.MakeCamera();
    await _db.Cameras.CreateAsync(camera);
    var stream = SqliteTestFixture.MakeStream(camera.Id);
    _streamId = stream.Id;
    await _db.Streams.UpsertAsync(stream);
  }

  [TearDown]
  public void TearDown() => _fixture.TearDown();

  /// <summary>
  /// SCENARIO:
  /// Three segments exist spanning 1000-4000
  ///
  /// ACTION:
  /// Query time range 1500-2500
  ///
  /// EXPECTED RESULT:
  /// Returns the two segments that overlap the range, ordered by start time
  /// </summary>
  [Test]
  public async Task GetByTimeRange_ReturnsOverlappingSegments()
  {
    var seg1 = SqliteTestFixture.MakeSegment(_streamId, 1000, 2000);
    var seg2 = SqliteTestFixture.MakeSegment(_streamId, 2000, 3000);
    var seg3 = SqliteTestFixture.MakeSegment(_streamId, 3000, 4000);
    await _db.Segments.CreateAsync(seg1);
    await _db.Segments.CreateAsync(seg2);
    await _db.Segments.CreateAsync(seg3);

    (await _db.Segments.GetByTimeRangeAsync(_streamId, 1500, 2500)).Switch(
      range =>
      {
        Assert.That(range, Has.Count.EqualTo(2));
        Assert.That(range[0].Id, Is.EqualTo(seg1.Id));
        Assert.That(range[1].Id, Is.EqualTo(seg2.Id));
      },
      error => Assert.Fail($"GetByTimeRange failed: {error.Message}"));
  }

  /// <summary>
  /// SCENARIO:
  /// Three segments with different sizes inserted out of order
  ///
  /// ACTION:
  /// GetOldest with limit 2
  ///
  /// EXPECTED RESULT:
  /// Returns the two segments with the earliest start times
  /// </summary>
  [Test]
  public async Task GetOldest_ReturnsEarliestByStartTime()
  {
    await _db.Segments.CreateAsync(SqliteTestFixture.MakeSegment(_streamId, 3000, 4000, 500));
    await _db.Segments.CreateAsync(SqliteTestFixture.MakeSegment(_streamId, 1000, 2000, 300));
    await _db.Segments.CreateAsync(SqliteTestFixture.MakeSegment(_streamId, 2000, 3000, 200));

    (await _db.Segments.GetOldestAsync(_streamId, 2)).Switch(
      oldest =>
      {
        Assert.That(oldest, Has.Count.EqualTo(2));
        Assert.That(oldest[0].StartTime, Is.EqualTo(1000UL));
        Assert.That(oldest[1].StartTime, Is.EqualTo(2000UL));
      },
      error => Assert.Fail($"GetOldest failed: {error.Message}"));
  }

  /// <summary>
  /// SCENARIO:
  /// Three segments with sizes 300, 200, 500
  ///
  /// ACTION:
  /// GetTotalSize
  ///
  /// EXPECTED RESULT:
  /// Returns 1000 (sum of all segment sizes)
  /// </summary>
  [Test]
  public async Task GetTotalSize_SumsAllSegments()
  {
    await _db.Segments.CreateAsync(SqliteTestFixture.MakeSegment(_streamId, 1000, 2000, 300));
    await _db.Segments.CreateAsync(SqliteTestFixture.MakeSegment(_streamId, 2000, 3000, 200));
    await _db.Segments.CreateAsync(SqliteTestFixture.MakeSegment(_streamId, 3000, 4000, 500));

    (await _db.Segments.GetTotalSizeAsync(_streamId)).Switch(
      total => Assert.That(total, Is.EqualTo(1000)),
      error => Assert.Fail($"GetTotalSize failed: {error.Message}"));
  }

  /// <summary>
  /// SCENARIO:
  /// A segment exists with keyframes
  ///
  /// ACTION:
  /// DeleteBatch the segment
  ///
  /// EXPECTED RESULT:
  /// Both the segment and its keyframes are removed
  /// </summary>
  [Test]
  public async Task DeleteBatch_CascadesToKeyframes()
  {
    var seg = SqliteTestFixture.MakeSegment(_streamId, 1000, 2000);
    await _db.Segments.CreateAsync(seg);

    await _db.Keyframes.CreateBatchAsync(
    [
      new Keyframe { SegmentId = seg.Id, Timestamp = 1000, ByteOffset = 0 },
      new Keyframe { SegmentId = seg.Id, Timestamp = 1500, ByteOffset = 50000 }
    ]);

    await _db.Segments.DeleteBatchAsync([seg.Id]);

    (await _db.Keyframes.GetBySegmentIdAsync(seg.Id)).Switch(
      kfs => Assert.That(kfs, Is.Empty),
      error => Assert.Fail($"GetBySegmentId failed: {error.Message}"));
  }

  /// <summary>
  /// SCENARIO:
  /// No segments exist for the stream
  ///
  /// ACTION:
  /// GetTotalSize
  ///
  /// EXPECTED RESULT:
  /// Returns 0
  /// </summary>
  [Test]
  public async Task GetTotalSize_EmptyReturnsZero()
  {
    (await _db.Segments.GetTotalSizeAsync(_streamId)).Switch(
      total => Assert.That(total, Is.EqualTo(0)),
      error => Assert.Fail($"GetTotalSize failed: {error.Message}"));
  }
}
