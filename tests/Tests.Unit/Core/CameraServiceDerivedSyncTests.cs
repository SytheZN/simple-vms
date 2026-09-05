using System.Runtime.Loader;
using Microsoft.Extensions.Logging.Abstractions;
using Server.Core.Services;
using Server.Plugins;
using Shared.Models.Events;
using Tests.Unit.Mocks;

namespace Tests.Unit.Core;

[TestFixture]
public class CameraServiceDerivedSyncTests
{
  /// <summary>
  /// SCENARIO:
  /// Analyzer declares a derived stream spec that has no existing row
  ///
  /// ACTION:
  /// Run derived-stream sync for the camera
  ///
  /// EXPECTED RESULT:
  /// A new active stream row is upserted with ProducerId, ParentStreamId, Kind, FormatId,
  /// and an Add entry appears in the diff
  /// </summary>
  [Test]
  public async Task CreatesNewRow_WhenSpecHasNoExisting()
  {
    var cameraId = Guid.NewGuid();
    var sourceId = Guid.NewGuid();
    var source = MakeStream(cameraId, sourceId, "main");

    var streams = new FakeStreamRepo();
    streams.AddStream(source);

    var analyzer = new FakeAnalyzer("motion-grid-h264",
      [new DerivedStreamSpec
      {
        ParentProfile = "main",
        Profile = "motion-grid-main",
        Kind = StreamKind.Metadata,
        FormatId = "motion-grid"
      }]);

    var host = MakeHost(streams, analyzer);
    var diff = new Dictionary<string, DiffChange>();

    await CameraService.SyncDerivedStreamsAsync(host, cameraId, diff, NullLogger.Instance, CancellationToken.None);

    Assert.That(streams.Upserts, Has.Count.EqualTo(1));
    var row = streams.Upserts[0];
    Assert.That(row.Profile, Is.EqualTo("motion-grid-main"));
    Assert.That(row.ProducerId, Is.EqualTo("motion-grid-h264"));
    Assert.That(row.ParentStreamId, Is.EqualTo(sourceId));
    Assert.That(row.Kind, Is.EqualTo(StreamKind.Metadata));
    Assert.That(row.FormatId, Is.EqualTo("motion-grid"));
    Assert.That(row.DeletedAt, Is.Null);
    Assert.That(diff, Has.Count.EqualTo(1));
    Assert.That(diff.Values.First().Type, Is.EqualTo(DiffChangeType.Add));
  }

  /// <summary>
  /// SCENARIO:
  /// A soft-deleted row exists with the same (ProducerId, Profile) as the analyzer's redeclared spec
  ///
  /// ACTION:
  /// Run derived-stream sync
  ///
  /// EXPECTED RESULT:
  /// The existing row is upserted with DeletedAt cleared (resurrected); same Id preserved
  /// </summary>
  [Test]
  public async Task ResurrectsSoftDeletedRow_WhenSpecMatches()
  {
    var cameraId = Guid.NewGuid();
    var sourceId = Guid.NewGuid();
    var derivedId = Guid.NewGuid();
    var source = MakeStream(cameraId, sourceId, "main");
    var derived = MakeStream(cameraId, derivedId, "motion-grid-main");
    derived.Kind = StreamKind.Metadata;
    derived.FormatId = "motion-grid";
    derived.ProducerId = "motion-grid-h264";
    derived.ParentStreamId = sourceId;
    derived.DeletedAt = (ulong)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000);

    var streams = new FakeStreamRepo();
    streams.AddStream(source);
    streams.AddStream(derived);

    var analyzer = new FakeAnalyzer("motion-grid-h264",
      [new DerivedStreamSpec
      {
        ParentProfile = "main",
        Profile = "motion-grid-main",
        Kind = StreamKind.Metadata,
        FormatId = "motion-grid"
      }]);

    var host = MakeHost(streams, analyzer);
    var diff = new Dictionary<string, DiffChange>();

    await CameraService.SyncDerivedStreamsAsync(host, cameraId, diff, NullLogger.Instance, CancellationToken.None);

    Assert.That(streams.Upserts, Has.Count.EqualTo(1));
    Assert.That(streams.Upserts[0].Id, Is.EqualTo(derivedId));
    Assert.That(streams.Upserts[0].DeletedAt, Is.Null);
  }

  /// <summary>
  /// SCENARIO:
  /// An active row exists with a producer id that the analyzer no longer declares a spec for
  ///
  /// ACTION:
  /// Run derived-stream sync
  ///
  /// EXPECTED RESULT:
  /// The row is upserted with DeletedAt set (soft-deleted) and a Remove entry appears in the diff
  /// </summary>
  [Test]
  public async Task SoftDeletesRow_WhenSpecNoLongerDeclared()
  {
    var cameraId = Guid.NewGuid();
    var sourceId = Guid.NewGuid();
    var derivedId = Guid.NewGuid();
    var source = MakeStream(cameraId, sourceId, "main");
    var derived = MakeStream(cameraId, derivedId, "motion-grid-main");
    derived.Kind = StreamKind.Metadata;
    derived.FormatId = "motion-grid";
    derived.ProducerId = "motion-grid-h264";
    derived.ParentStreamId = sourceId;

    var streams = new FakeStreamRepo();
    streams.AddStream(source);
    streams.AddStream(derived);

    var analyzer = new FakeAnalyzer("motion-grid-h264", []);

    var host = MakeHost(streams, analyzer);
    var diff = new Dictionary<string, DiffChange>();

    await CameraService.SyncDerivedStreamsAsync(host, cameraId, diff, NullLogger.Instance, CancellationToken.None);

    Assert.That(streams.Upserts, Has.Count.EqualTo(1));
    Assert.That(streams.Upserts[0].Id, Is.EqualTo(derivedId));
    Assert.That(streams.Upserts[0].DeletedAt, Is.Not.Null);
    Assert.That(diff, Has.Count.EqualTo(1));
    Assert.That(diff.Values.First().Type, Is.EqualTo(DiffChangeType.Remove));
  }

  /// <summary>
  /// SCENARIO:
  /// Analyzer declares spec referencing a parent profile that does not exist on the camera
  ///
  /// ACTION:
  /// Run derived-stream sync
  ///
  /// EXPECTED RESULT:
  /// No row is created or modified for that spec, no diff entry
  /// </summary>
  [Test]
  public async Task SkipsSpec_WhenParentProfileMissing()
  {
    var cameraId = Guid.NewGuid();
    var source = MakeStream(cameraId, Guid.NewGuid(), "main");

    var streams = new FakeStreamRepo();
    streams.AddStream(source);

    var analyzer = new FakeAnalyzer("motion-grid-h264",
      [new DerivedStreamSpec
      {
        ParentProfile = "ghost-profile",
        Profile = "motion-grid-ghost",
        Kind = StreamKind.Metadata,
        FormatId = "motion-grid"
      }]);

    var host = MakeHost(streams, analyzer);
    var diff = new Dictionary<string, DiffChange>();

    await CameraService.SyncDerivedStreamsAsync(host, cameraId, diff, NullLogger.Instance, CancellationToken.None);

    Assert.That(streams.Upserts, Is.Empty);
    Assert.That(diff, Is.Empty);
  }

  private static CameraStream MakeStream(Guid cameraId, Guid streamId, string profile) => new()
  {
    Id = streamId,
    CameraId = cameraId,
    Profile = profile,
    Kind = StreamKind.Quality,
    FormatId = "fmp4",
    Codec = "h264",
    Uri = "rtsp://test"
  };

  private sealed class FakeAnalyzer : IPlugin, IDataStreamAnalyzer, IDataStreamAnalyzerStreamOutput
  {
    private readonly IReadOnlyList<DerivedStreamSpec> _specs;

    public FakeAnalyzer(string analyzerId, IReadOnlyList<DerivedStreamSpec> specs)
    {
      AnalyzerId = analyzerId;
      _specs = specs;
      Metadata = new PluginMetadata { Id = analyzerId, Name = analyzerId, Version = "1.0.0", Description = "" };
    }

    public PluginMetadata Metadata { get; }
    public string AnalyzerId { get; }
    public IReadOnlyList<string> SupportedCodecs => ["h264"];

    public OneOf<Success, Error> Initialize(PluginContext context) => new Success();
    public Task<OneOf<Success, Error>> StartAsync(CancellationToken ct) =>
      Task.FromResult<OneOf<Success, Error>>(new Success());
    public Task<OneOf<Success, Error>> StopAsync(CancellationToken ct) =>
      Task.FromResult<OneOf<Success, Error>>(new Success());

    public IReadOnlyList<DerivedStreamSpec> GetDerivedStreams(Guid cameraId) => _specs;
    public Task<OneOf<IDataStream, Error>> StartStreamAsync(Guid cameraId, string parentProfile, CancellationToken ct) =>
      throw new NotImplementedException();
    public bool NeedsRebuild(Guid cameraId, string parentProfile) => false;
  }

  private sealed class FakeStreamRepo : IStreamRepository
  {
    private readonly List<CameraStream> _streams = [];
    public List<CameraStream> Upserts { get; } = [];

    public void AddStream(CameraStream stream) => _streams.Add(stream);

    public Task<OneOf<IReadOnlyList<CameraStream>, Error>> GetByCameraIdAsync(Guid cameraId, CancellationToken ct) =>
      Task.FromResult<OneOf<IReadOnlyList<CameraStream>, Error>>(
        _streams.Where(s => s.CameraId == cameraId).ToList());

    public Task<OneOf<CameraStream, Error>> GetByIdAsync(Guid id, CancellationToken ct) =>
      throw new NotImplementedException();

    public Task<OneOf<Success, Error>> UpsertAsync(CameraStream stream, CancellationToken ct)
    {
      Upserts.Add(stream);
      var idx = _streams.FindIndex(s => s.Id == stream.Id);
      if (idx >= 0) _streams[idx] = stream;
      else _streams.Add(stream);
      return Task.FromResult<OneOf<Success, Error>>(new Success());
    }

    public Task<OneOf<Success, Error>> DeleteAsync(Guid id, CancellationToken ct)
    {
      _streams.RemoveAll(s => s.Id == id);
      return Task.FromResult<OneOf<Success, Error>>(new Success());
    }
  }

  private static FakePluginHost MakeHost(FakeStreamRepo streams, FakeAnalyzer analyzer) => new()
  {
    DataProvider = new MinimalDataProvider(streams),
    Analyzers = [analyzer],
    Plugins = [new PluginEntry
    {
      PluginType = analyzer.GetType(),
      LoadContext = AssemblyLoadContext.Default,
      Plugin = analyzer,
      Metadata = analyzer.Metadata
    }]
  };

  private sealed class MinimalDataProvider : IDataProvider
  {
    public string ProviderId => "fake";
    public ICameraRepository Cameras => throw new NotImplementedException();
    public IStreamRepository Streams { get; }
    public ISegmentRepository Segments => throw new NotImplementedException();
    public IKeyframeRepository Keyframes => throw new NotImplementedException();
    public IEventRepository Events => throw new NotImplementedException();
    public ISystemEventRepository SystemEvents => throw new NotImplementedException();
    public IClientRepository Clients => throw new NotImplementedException();
    public IConfigRepository Config => throw new NotImplementedException();
    public IDataStore GetDataStore(string pluginId) => throw new NotImplementedException();

    public MinimalDataProvider(IStreamRepository streams) => Streams = streams;
  }
}
