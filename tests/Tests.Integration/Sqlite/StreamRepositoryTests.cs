using Shared.Models;

namespace Tests.Integration.Sqlite;

[TestFixture]
public sealed class StreamRepositoryTests
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
  /// A camera exists in the database
  ///
  /// ACTION:
  /// Upsert a stream, then retrieve it by ID
  ///
  /// EXPECTED RESULT:
  /// All fields round-trip correctly
  /// </summary>
  [Test]
  public async Task Upsert_CreatesAndGetByIdReturnsFields()
  {
    var camera = SqliteTestFixture.MakeCamera();
    await _db.Cameras.CreateAsync(camera);

    var stream = SqliteTestFixture.MakeStream(camera.Id);
    await _db.Streams.UpsertAsync(stream);

    (await _db.Streams.GetByIdAsync(stream.Id)).Switch(
      fetched =>
      {
        Assert.That(fetched.Profile, Is.EqualTo("main"));
        Assert.That(fetched.Codec, Is.EqualTo("h264"));
        Assert.That(fetched.Resolution, Is.EqualTo("1920x1080"));
        Assert.That(fetched.Fps, Is.EqualTo(30));
        Assert.That(fetched.RetentionMode, Is.EqualTo(RetentionMode.Default));
      },
      error => Assert.Fail($"GetById failed: {error.Message}"));
  }

  /// <summary>
  /// SCENARIO:
  /// A stream already exists
  ///
  /// ACTION:
  /// Upsert the same stream with changed fields
  ///
  /// EXPECTED RESULT:
  /// The updated fields are persisted
  /// </summary>
  [Test]
  public async Task Upsert_UpdatesExistingStream()
  {
    var camera = SqliteTestFixture.MakeCamera();
    await _db.Cameras.CreateAsync(camera);
    var stream = SqliteTestFixture.MakeStream(camera.Id);
    await _db.Streams.UpsertAsync(stream);

    stream.Codec = "h265";
    stream.RetentionMode = RetentionMode.Bytes;
    stream.RetentionValue = 1073741824;
    await _db.Streams.UpsertAsync(stream);

    (await _db.Streams.GetByIdAsync(stream.Id)).Switch(
      fetched =>
      {
        Assert.That(fetched.Codec, Is.EqualTo("h265"));
        Assert.That(fetched.RetentionMode, Is.EqualTo(RetentionMode.Bytes));
        Assert.That(fetched.RetentionValue, Is.EqualTo(1073741824));
      },
      error => Assert.Fail($"GetById failed: {error.Message}"));
  }

  /// <summary>
  /// SCENARIO:
  /// A camera has one stream
  ///
  /// ACTION:
  /// GetByCameraId
  ///
  /// EXPECTED RESULT:
  /// Returns a list with exactly one stream
  /// </summary>
  [Test]
  public async Task GetByCameraId_ReturnsStreams()
  {
    var camera = SqliteTestFixture.MakeCamera();
    await _db.Cameras.CreateAsync(camera);
    await _db.Streams.UpsertAsync(SqliteTestFixture.MakeStream(camera.Id));

    (await _db.Streams.GetByCameraIdAsync(camera.Id)).Switch(
      streams => Assert.That(streams, Has.Count.EqualTo(1)),
      error => Assert.Fail($"GetByCameraId failed: {error.Message}"));
  }

  /// <summary>
  /// SCENARIO:
  /// A stream exists
  ///
  /// ACTION:
  /// Delete it, then GetById
  ///
  /// EXPECTED RESULT:
  /// Delete succeeds, GetById returns NotFound
  /// </summary>
  [Test]
  public async Task Delete_RemovesStream()
  {
    var camera = SqliteTestFixture.MakeCamera();
    await _db.Cameras.CreateAsync(camera);
    var stream = SqliteTestFixture.MakeStream(camera.Id);
    await _db.Streams.UpsertAsync(stream);

    await _db.Streams.DeleteAsync(stream.Id);

    (await _db.Streams.GetByIdAsync(stream.Id)).Switch(
      _ => Assert.Fail("Expected NotFound after delete"),
      error => Assert.That(error.Result, Is.EqualTo(Result.NotFound)));
  }
}
