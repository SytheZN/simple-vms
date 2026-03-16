using Shared.Models;

namespace Tests.Integration.Sqlite;

[TestFixture]
public sealed class ClientRepositoryTests
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
  /// Create a client and retrieve by ID
  ///
  /// EXPECTED RESULT:
  /// All fields round-trip correctly
  /// </summary>
  [Test]
  public async Task CreateAndGetById_RoundTrips()
  {
    var client = MakeClient();
    await _db.Clients.CreateAsync(client);

    (await _db.Clients.GetByIdAsync(client.Id)).Switch(
      fetched =>
      {
        Assert.That(fetched.Name, Is.EqualTo("My Phone"));
        Assert.That(fetched.CertificateSerial, Is.EqualTo("ABCD1234"));
        Assert.That(fetched.Revoked, Is.False);
        Assert.That(fetched.EnrolledAt, Is.EqualTo(1710000000000000UL));
      },
      error => Assert.Fail($"GetById failed: {error.Message}"));
  }

  /// <summary>
  /// SCENARIO:
  /// A client exists
  ///
  /// ACTION:
  /// Look up by certificate serial
  ///
  /// EXPECTED RESULT:
  /// Returns the matching client
  /// </summary>
  [Test]
  public async Task GetByCertificateSerial_Finds()
  {
    var client = MakeClient();
    await _db.Clients.CreateAsync(client);

    (await _db.Clients.GetByCertificateSerialAsync("ABCD1234")).Switch(
      found => Assert.That(found.Id, Is.EqualTo(client.Id)),
      error => Assert.Fail($"GetByCertificateSerial failed: {error.Message}"));
  }

  /// <summary>
  /// SCENARIO:
  /// A client has been revoked
  ///
  /// ACTION:
  /// GetAll (which excludes revoked) and GetById (which excludes revoked)
  ///
  /// EXPECTED RESULT:
  /// Both return empty/NotFound. GetByCertificateSerial still finds it with Revoked=true.
  /// </summary>
  [Test]
  public async Task Revoked_ExcludedFromGetAllAndGetById()
  {
    var client = MakeClient();
    await _db.Clients.CreateAsync(client);

    client.Revoked = true;
    await _db.Clients.UpdateAsync(client);

    (await _db.Clients.GetAllAsync()).Switch(
      all => Assert.That(all, Is.Empty),
      error => Assert.Fail($"GetAll failed: {error.Message}"));

    (await _db.Clients.GetByIdAsync(client.Id)).Switch(
      _ => Assert.Fail("Expected NotFound for revoked client"),
      error => Assert.That(error.Result, Is.EqualTo(Result.NotFound)));

    (await _db.Clients.GetByCertificateSerialAsync("ABCD1234")).Switch(
      bySerial => Assert.That(bySerial.Revoked, Is.True),
      error => Assert.Fail($"GetByCertificateSerial failed: {error.Message}"));
  }

  /// <summary>
  /// SCENARIO:
  /// A client exists
  ///
  /// ACTION:
  /// Update its name
  ///
  /// EXPECTED RESULT:
  /// GetById returns the updated name
  /// </summary>
  [Test]
  public async Task Update_ChangesName()
  {
    var client = MakeClient();
    await _db.Clients.CreateAsync(client);

    client.Name = "Kitchen Tablet";
    await _db.Clients.UpdateAsync(client);

    (await _db.Clients.GetByIdAsync(client.Id)).Switch(
      fetched => Assert.That(fetched.Name, Is.EqualTo("Kitchen Tablet")),
      error => Assert.Fail($"GetById failed: {error.Message}"));
  }

  private static Client MakeClient() => new()
  {
    Id = Guid.NewGuid(),
    Name = "My Phone",
    CertificateSerial = "ABCD1234",
    EnrolledAt = 1710000000000000
  };
}
