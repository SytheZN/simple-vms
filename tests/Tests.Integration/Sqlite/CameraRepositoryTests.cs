using Shared.Models;

namespace Tests.Integration.Sqlite;

[TestFixture]
public sealed class CameraRepositoryTests
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
  /// Empty database
  ///
  /// ACTION:
  /// Create a camera, then retrieve it by ID
  ///
  /// EXPECTED RESULT:
  /// All fields round-trip correctly including capabilities, config, and retention
  /// </summary>
  [Test]
  public async Task CreateAndGetById_RoundTripsAllFields()
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

    (await _db.Cameras.CreateAsync(camera)).Switch(
      _ => { },
      error => Assert.Fail($"Create failed: {error.Message}"));

    (await _db.Cameras.GetByIdAsync(camera.Id)).Switch(
      fetched =>
      {
        Assert.That(fetched.Name, Is.EqualTo("Front Door"));
        Assert.That(fetched.Address, Is.EqualTo("192.168.1.100"));
        Assert.That(fetched.ProviderId, Is.EqualTo("onvif"));
        Assert.That(fetched.Capabilities, Is.EqualTo(new[] { "ptz", "audio" }));
        Assert.That(fetched.Config["key"], Is.EqualTo("value"));
        Assert.That(fetched.RetentionMode, Is.EqualTo(RetentionMode.Days));
        Assert.That(fetched.RetentionValue, Is.EqualTo(30));
        Assert.That(fetched.CreatedAt, Is.EqualTo(1710000000000000UL));
      },
      error => Assert.Fail($"GetById failed: {error.Message}"));
  }

  /// <summary>
  /// SCENARIO:
  /// A camera exists in the database
  ///
  /// ACTION:
  /// Look it up by address
  ///
  /// EXPECTED RESULT:
  /// Returns the matching camera
  /// </summary>
  [Test]
  public async Task GetByAddress_FindsCamera()
  {
    var camera = SqliteTestFixture.MakeCamera("10.0.0.50");
    await _db.Cameras.CreateAsync(camera);

    (await _db.Cameras.GetByAddressAsync("10.0.0.50")).Switch(
      found => Assert.That(found.Id, Is.EqualTo(camera.Id)),
      error => Assert.Fail($"GetByAddress failed: {error.Message}"));
  }

  /// <summary>
  /// SCENARIO:
  /// No camera with the given address exists
  ///
  /// ACTION:
  /// Look up by a non-existent address
  ///
  /// EXPECTED RESULT:
  /// NotFound error
  /// </summary>
  [Test]
  public async Task GetByAddress_NotFound()
  {
    (await _db.Cameras.GetByAddressAsync("99.99.99.99")).Switch(
      _ => Assert.Fail("Expected NotFound"),
      error => Assert.That(error.Result, Is.EqualTo(Result.NotFound)));
  }

  /// <summary>
  /// SCENARIO:
  /// A camera exists in the database
  ///
  /// ACTION:
  /// Update its name and retrieve again
  ///
  /// EXPECTED RESULT:
  /// The name reflects the update
  /// </summary>
  [Test]
  public async Task Update_ChangesFields()
  {
    var camera = SqliteTestFixture.MakeCamera();
    await _db.Cameras.CreateAsync(camera);

    camera.Name = "Back Door";
    camera.UpdatedAt = 1710000001000000;
    await _db.Cameras.UpdateAsync(camera);

    (await _db.Cameras.GetByIdAsync(camera.Id)).Switch(
      fetched =>
      {
        Assert.That(fetched.Name, Is.EqualTo("Back Door"));
        Assert.That(fetched.UpdatedAt, Is.EqualTo(1710000001000000UL));
      },
      error => Assert.Fail($"GetById failed: {error.Message}"));
  }

  /// <summary>
  /// SCENARIO:
  /// One camera exists in the database
  ///
  /// ACTION:
  /// GetAll
  ///
  /// EXPECTED RESULT:
  /// Returns a list with exactly one camera
  /// </summary>
  [Test]
  public async Task GetAll_ReturnsAllCameras()
  {
    await _db.Cameras.CreateAsync(SqliteTestFixture.MakeCamera());

    (await _db.Cameras.GetAllAsync()).Switch(
      all => Assert.That(all, Has.Count.EqualTo(1)),
      error => Assert.Fail($"GetAll failed: {error.Message}"));
  }

  /// <summary>
  /// SCENARIO:
  /// A camera exists in the database
  ///
  /// ACTION:
  /// Delete it, then try to retrieve by ID
  ///
  /// EXPECTED RESULT:
  /// Delete succeeds, subsequent GetById returns NotFound
  /// </summary>
  [Test]
  public async Task Delete_RemovesCamera()
  {
    var camera = SqliteTestFixture.MakeCamera();
    await _db.Cameras.CreateAsync(camera);

    (await _db.Cameras.DeleteAsync(camera.Id)).Switch(
      _ => { },
      error => Assert.Fail($"Delete failed: {error.Message}"));

    (await _db.Cameras.GetByIdAsync(camera.Id)).Switch(
      _ => Assert.Fail("Expected NotFound after delete"),
      error => Assert.That(error.Result, Is.EqualTo(Result.NotFound)));
  }

  /// <summary>
  /// SCENARIO:
  /// A camera exists with a stream attached
  ///
  /// ACTION:
  /// Delete the camera
  ///
  /// EXPECTED RESULT:
  /// The stream is also deleted (cascade)
  /// </summary>
  [Test]
  public async Task Delete_CascadesToStreams()
  {
    var camera = SqliteTestFixture.MakeCamera();
    await _db.Cameras.CreateAsync(camera);
    await _db.Streams.UpsertAsync(SqliteTestFixture.MakeStream(camera.Id));

    await _db.Cameras.DeleteAsync(camera.Id);

    (await _db.Streams.GetByCameraIdAsync(camera.Id)).Switch(
      streams => Assert.That(streams, Is.Empty),
      error => Assert.Fail($"GetByCameraId failed: {error.Message}"));
  }

  /// <summary>
  /// SCENARIO:
  /// No camera with the given ID exists
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
    (await _db.Cameras.GetByIdAsync(Guid.NewGuid())).Switch(
      _ => Assert.Fail("Expected NotFound"),
      error => Assert.That(error.Result, Is.EqualTo(Result.NotFound)));
  }
}
