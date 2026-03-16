using Shared.Models;

namespace Tests.Integration.Sqlite;

[TestFixture]
public sealed class KeyframeRepositoryTests
{
  private readonly SqliteTestFixture _fixture = new();
  private IDataProvider _db = null!;
  private Guid _segmentId;

  [SetUp]
  public async Task SetUp()
  {
    await _fixture.SetUp();
    _db = _fixture.Provider;

    var camera = SqliteTestFixture.MakeCamera();
    await _db.Cameras.CreateAsync(camera);
    var stream = SqliteTestFixture.MakeStream(camera.Id);
    await _db.Streams.UpsertAsync(stream);
    var seg = SqliteTestFixture.MakeSegment(stream.Id, 1000, 5000);
    _segmentId = seg.Id;
    await _db.Segments.CreateAsync(seg);

    await _db.Keyframes.CreateBatchAsync(
    [
      new Keyframe { SegmentId = _segmentId, Timestamp = 1000, ByteOffset = 0 },
      new Keyframe { SegmentId = _segmentId, Timestamp = 2000, ByteOffset = 50000 },
      new Keyframe { SegmentId = _segmentId, Timestamp = 3000, ByteOffset = 100000 },
      new Keyframe { SegmentId = _segmentId, Timestamp = 4000, ByteOffset = 150000 }
    ]);
  }

  [TearDown]
  public void TearDown() => _fixture.TearDown();

  /// <summary>
  /// SCENARIO:
  /// A segment has 4 keyframes at timestamps 1000, 2000, 3000, 4000
  ///
  /// ACTION:
  /// GetNearest with timestamp 2500
  ///
  /// EXPECTED RESULT:
  /// Returns the keyframe at 2000 (nearest at-or-before)
  /// </summary>
  [Test]
  public async Task GetNearest_ReturnsPrecedingKeyframe()
  {
    (await _db.Keyframes.GetNearestAsync(_segmentId, 2500)).Switch(
      nearest =>
      {
        Assert.That(nearest.Timestamp, Is.EqualTo(2000UL));
        Assert.That(nearest.ByteOffset, Is.EqualTo(50000));
      },
      error => Assert.Fail($"GetNearest failed: {error.Message}"));
  }

  /// <summary>
  /// SCENARIO:
  /// A segment has a keyframe at exactly timestamp 3000
  ///
  /// ACTION:
  /// GetNearest with timestamp 3000
  ///
  /// EXPECTED RESULT:
  /// Returns the exact keyframe at 3000
  /// </summary>
  [Test]
  public async Task GetNearest_ExactMatch()
  {
    (await _db.Keyframes.GetNearestAsync(_segmentId, 3000)).Switch(
      exact => Assert.That(exact.Timestamp, Is.EqualTo(3000UL)),
      error => Assert.Fail($"GetNearest exact failed: {error.Message}"));
  }

  /// <summary>
  /// SCENARIO:
  /// A segment's earliest keyframe is at timestamp 1000
  ///
  /// ACTION:
  /// GetNearest with timestamp 500 (before all keyframes)
  ///
  /// EXPECTED RESULT:
  /// NotFound because no keyframe exists at or before 500
  /// </summary>
  [Test]
  public async Task GetNearest_BeforeAllKeyframes_ReturnsNotFound()
  {
    (await _db.Keyframes.GetNearestAsync(_segmentId, 500)).Switch(
      _ => Assert.Fail("Expected NotFound for timestamp before all keyframes"),
      error => Assert.That(error.Result, Is.EqualTo(Result.NotFound)));
  }

  /// <summary>
  /// SCENARIO:
  /// A segment has 4 keyframes
  ///
  /// ACTION:
  /// GetBySegmentId
  ///
  /// EXPECTED RESULT:
  /// Returns all 4 keyframes ordered by timestamp
  /// </summary>
  [Test]
  public async Task GetBySegmentId_ReturnsAllOrdered()
  {
    (await _db.Keyframes.GetBySegmentIdAsync(_segmentId)).Switch(
      kfs =>
      {
        Assert.That(kfs, Has.Count.EqualTo(4));
        Assert.That(kfs[0].Timestamp, Is.EqualTo(1000UL));
        Assert.That(kfs[3].Timestamp, Is.EqualTo(4000UL));
      },
      error => Assert.Fail($"GetBySegmentId failed: {error.Message}"));
  }

  /// <summary>
  /// SCENARIO:
  /// A segment has keyframes
  ///
  /// ACTION:
  /// DeleteBySegmentIds for that segment
  ///
  /// EXPECTED RESULT:
  /// All keyframes for the segment are removed
  /// </summary>
  [Test]
  public async Task DeleteBySegmentIds_RemovesKeyframes()
  {
    await _db.Keyframes.DeleteBySegmentIdsAsync([_segmentId]);

    (await _db.Keyframes.GetBySegmentIdAsync(_segmentId)).Switch(
      kfs => Assert.That(kfs, Is.Empty),
      error => Assert.Fail($"GetBySegmentId failed: {error.Message}"));
  }
}
