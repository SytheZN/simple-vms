using Microsoft.Extensions.Logging.Abstractions;
using Server.Core;
using Server.Core.Services;
using Server.Core.PortForwarding;
using Server.Plugins;
using Shared.Models;
using Shared.Models.Dto;
using Tests.Unit.Streaming;

namespace Tests.Unit.Core;

[TestFixture]
public class SystemServiceRecomputeTests
{
  /// <summary>
  /// SCENARIO:
  /// Bootstrap calls RecomputeMissingSettingsAsync during the "starting"
  /// transition, after the data provider has come up but before the final
  /// TransitionToHealthy. This is the path the setup wizard depends on to
  /// detect that required settings are still unset.
  ///
  /// ACTION:
  /// RecomputeMissingSettingsAsync runs while status == "starting"
  ///
  /// EXPECTED RESULT:
  /// The data provider is read and MissingSettings is populated. Any guard
  /// that treats "starting" as pre-ready breaks the wizard redirect to
  /// /setup/complete.
  /// </summary>
  [Test]
  public async Task StartingState_StillComputes()
  {
    var health = BuildHealth("starting");
    var provider = new RecordingDataProvider(new Dictionary<string, string>());
    var service = BuildService(provider, health);

    await service.RecomputeMissingSettingsAsync(CancellationToken.None);

    Assert.That(provider.GetAllCallCount, Is.EqualTo(1));
    Assert.That(health.MissingSettings, Contains.Item("internalEndpoint"));
  }

  /// <summary>
  /// SCENARIO:
  /// Server is healthy and the data provider returns a settings snapshot that
  /// is missing the internal endpoint
  ///
  /// ACTION:
  /// RecomputeMissingSettingsAsync runs
  ///
  /// EXPECTED RESULT:
  /// MissingSettings is populated with the internalEndpoint key
  /// </summary>
  [Test]
  public async Task Healthy_PopulatesMissingFromProvider()
  {
    var health = BuildHealth("healthy");
    var provider = new RecordingDataProvider(new Dictionary<string, string>());
    var service = BuildService(provider, health);

    await service.RecomputeMissingSettingsAsync(CancellationToken.None);

    Assert.That(provider.GetAllCallCount, Is.EqualTo(1));
    Assert.That(health.MissingSettings, Contains.Item("internalEndpoint"));
  }

  /// <summary>
  /// SCENARIO:
  /// Server is healthy but the data provider throws while serving the read
  ///
  /// ACTION:
  /// RecomputeMissingSettingsAsync runs
  ///
  /// EXPECTED RESULT:
  /// The previous cached value is preserved - a transient failure must not
  /// clobber a good snapshot with null
  /// </summary>
  [Test]
  public async Task Healthy_ProviderThrows_LeavesCacheUntouched()
  {
    var health = BuildHealth("healthy");
    var previous = new[] { "internalEndpoint" };
    health.SetMissingSettings(previous);

    var provider = new RecordingDataProvider(throwOnGetAll: true);
    var service = BuildService(provider, health);

    await service.RecomputeMissingSettingsAsync(CancellationToken.None);

    Assert.That(health.MissingSettings, Is.SameAs(previous));
  }

  private static SystemService BuildService(IDataProvider provider, SystemHealth health)
  {
    var host = new SessionTestPluginHost(provider);
    var endpoints = new ServerEndpoints { TunnelPort = 4433 };
    return new SystemService(
      host, health, endpoints,
      new NoopPortForwardingApplier(),
      new StubHttpClientFactory(),
      NullLogger<SystemService>.Instance);
  }

  private static SystemHealth BuildHealth(string status)
  {
    var health = new SystemHealth();
    switch (status)
    {
      case "starting": health.TransitionToStarting(); break;
      case "degraded": health.TransitionToDegraded(); break;
      case "healthy": health.TransitionToHealthy(); break;
    }
    return health;
  }

  private sealed class NoopPortForwardingApplier : IPortForwardingApplier
  {
    public Task<OneOf<Success, Error>> ApplyAsync(CancellationToken ct) =>
      Task.FromResult<OneOf<Success, Error>>(new Success());
    public PortForwardingStatus GetStatus() => new() { Active = false };
  }

  private sealed class StubHttpClientFactory : IHttpClientFactory
  {
    public HttpClient CreateClient(string name) => new();
  }

  private sealed class RecordingDataProvider : IDataProvider
  {
    public int GetAllCallCount => _config.GetAllCallCount;
    private readonly RecordingConfigRepository _config;
    public RecordingDataProvider(Dictionary<string, string> snapshot)
    {
      _config = new RecordingConfigRepository(snapshot);
    }
    public RecordingDataProvider(bool throwOnGetAll)
    {
      _config = new RecordingConfigRepository(new Dictionary<string, string>())
      {
        ThrowOnGetAll = throwOnGetAll
      };
    }
    public string ProviderId => "test";
    public ICameraRepository Cameras => null!;
    public IStreamRepository Streams => null!;
    public ISegmentRepository Segments => null!;
    public IKeyframeRepository Keyframes => null!;
    public IEventRepository Events => null!;
    public IClientRepository Clients => null!;
    public IConfigRepository Config => _config;
    public IDataStore GetDataStore(string pluginId) => null!;
  }

  private sealed class RecordingConfigRepository : IConfigRepository
  {
    private readonly Dictionary<string, string> _snapshot;
    public int GetAllCallCount { get; private set; }
    public bool ThrowOnGetAll { get; set; }
    public RecordingConfigRepository(Dictionary<string, string> snapshot) { _snapshot = snapshot; }

    public Task<OneOf<string?, Error>> GetAsync(string pluginId, string key, CancellationToken ct) =>
      Task.FromResult<OneOf<string?, Error>>(_snapshot.TryGetValue(key, out var v) ? v : null);

    public Task<OneOf<IReadOnlyDictionary<string, string>, Error>> GetAllAsync(
      string pluginId, CancellationToken ct)
    {
      GetAllCallCount++;
      if (ThrowOnGetAll) throw new InvalidOperationException("boom");
      return Task.FromResult<OneOf<IReadOnlyDictionary<string, string>, Error>>(_snapshot);
    }

    public Task<OneOf<Success, Error>> SetAsync(
      string pluginId, string key, string value, CancellationToken ct) =>
      Task.FromResult<OneOf<Success, Error>>(new Success());

    public Task<OneOf<Success, Error>> DeleteAsync(
      string pluginId, string key, CancellationToken ct) =>
      Task.FromResult<OneOf<Success, Error>>(new Success());
  }
}
