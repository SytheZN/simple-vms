using Microsoft.Extensions.Logging.Abstractions;
using Server.Tunnel;
using Shared.Models;
using Tests.Unit.Streaming;

namespace Tests.Unit.Tunnel;

[TestFixture]
public class ClientValidationTests
{
  /// <summary>
  /// SCENARIO:
  /// A client certificate serial matches a non-revoked client in the database
  ///
  /// ACTION:
  /// Validate the serial
  ///
  /// EXPECTED RESULT:
  /// Returns the client's Id
  /// </summary>
  [Test]
  public async Task Validate_ValidSerial_ReturnsClientId()
  {
    var clientId = Guid.NewGuid();
    var client = new Shared.Models.Client
    {
      Id = clientId,
      Name = "test",
      CertificateSerial = "abcd1234",
      EnrolledAt = 1000
    };

    var validator = CreateValidator(new FakeClientRepository(client));
    var result = await validator.ValidateAsync("abcd1234", CancellationToken.None);

    Assert.That(result.IsT0, Is.True);
    Assert.That(result.AsT0, Is.EqualTo(clientId));
  }

  /// <summary>
  /// SCENARIO:
  /// A client certificate serial is not found in the database
  ///
  /// ACTION:
  /// Validate an unknown serial
  ///
  /// EXPECTED RESULT:
  /// Returns an error with Result.Unauthorized
  /// </summary>
  [Test]
  public async Task Validate_UnknownSerial_ReturnsUnauthorized()
  {
    var validator = CreateValidator(new FakeClientRepository(null));
    var result = await validator.ValidateAsync("unknown", CancellationToken.None);

    Assert.That(result.IsT1, Is.True);
    Assert.That(result.AsT1.Result, Is.EqualTo(Result.Unauthorized));
  }

  /// <summary>
  /// SCENARIO:
  /// A client certificate serial matches a revoked client
  ///
  /// ACTION:
  /// Validate the revoked client's serial
  ///
  /// EXPECTED RESULT:
  /// Returns an error with Result.Forbidden
  /// </summary>
  [Test]
  public async Task Validate_RevokedClient_ReturnsForbidden()
  {
    var client = new Shared.Models.Client
    {
      Id = Guid.NewGuid(),
      Name = "revoked",
      CertificateSerial = "revoked1234",
      Revoked = true,
      EnrolledAt = 1000
    };

    var validator = CreateValidator(new FakeClientRepository(client));
    var result = await validator.ValidateAsync("revoked1234", CancellationToken.None);

    Assert.That(result.IsT1, Is.True);
    Assert.That(result.AsT1.Result, Is.EqualTo(Result.Forbidden));
  }

  private static ClientValidator CreateValidator(IClientRepository clients)
  {
    var data = new FakeDataProvider(clients);
    var plugins = new SessionTestPluginHost(dataProvider: data);
    return new ClientValidator(plugins, NullLogger.Instance);
  }

  private sealed class FakeClientRepository : IClientRepository
  {
    private readonly Shared.Models.Client? _client;

    public FakeClientRepository(Shared.Models.Client? client) => _client = client;

    public Task<OneOf<Shared.Models.Client, Error>> GetByCertificateSerialAsync(string serial, CancellationToken ct)
    {
      if (_client != null && _client.CertificateSerial == serial)
        return Task.FromResult<OneOf<Shared.Models.Client, Error>>(_client);
      return Task.FromResult<OneOf<Shared.Models.Client, Error>>(
        Error.Create(ModuleIds.Tunnel, 0, Result.NotFound, "not found"));
    }

    public Task<OneOf<IReadOnlyList<Shared.Models.Client>, Error>> GetAllAsync(CancellationToken ct) =>
      throw new NotImplementedException();
    public Task<OneOf<Shared.Models.Client, Error>> GetByIdAsync(Guid id, CancellationToken ct) =>
      throw new NotImplementedException();
    public Task<OneOf<Success, Error>> CreateAsync(Shared.Models.Client client, CancellationToken ct) =>
      throw new NotImplementedException();
    public Task<OneOf<Success, Error>> UpdateAsync(Shared.Models.Client client, CancellationToken ct) =>
      throw new NotImplementedException();
  }

  private sealed class FakeDataProvider : IDataProvider
  {
    public FakeDataProvider(IClientRepository clients) => Clients = clients;
    public string ProviderId => "fake";
    public ICameraRepository Cameras => null!;
    public IStreamRepository Streams => null!;
    public ISegmentRepository Segments => null!;
    public IKeyframeRepository Keyframes => null!;
    public IEventRepository Events => null!;
    public IClientRepository Clients { get; }
    public IConfigRepository Config => null!;
    public IDataStore GetDataStore(string pluginId) => null!;
  }
}
