using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Analyzer.Thumbnail;
using Shared.Models;
using Shared.Models.Formats;

namespace Tests.Unit.Thumbnail;

[TestFixture]
public class ThumbnailPluginTests
{
  /// <summary>
  /// SCENARIO:
  /// A camera exposes a 1080p main stream and a 360p sub stream, with no source configured
  ///
  /// ACTION:
  /// Ask the analyzer for its derived streams
  ///
  /// EXPECTED RESULT:
  /// One metadata stream is declared, parented to the lowest-resolution stream, named after its
  /// parent, carrying the mjpeg format so StreamingService can build a pipeline for it, and
  /// marked non-recordable so the recorder leaves the live preview alone
  /// </summary>
  [Test]
  public void GetDerivedStreams_NoSourceConfigured_DefaultsToLowestResolution()
  {
    var plugin = NewPlugin(out _, Stream("main", "h264", "1920x1080"), Stream("sub", "h264", "640x360"));

    var specs = plugin.GetDerivedStreams(CameraId);

    Assert.That(specs, Has.Count.EqualTo(1));
    Assert.Multiple(() =>
    {
      Assert.That(specs[0].ParentProfile, Is.EqualTo("sub"));
      Assert.That(specs[0].Profile, Is.EqualTo("sub-thumbnail"));
      Assert.That(specs[0].Kind, Is.EqualTo(StreamKind.Metadata));
      Assert.That(specs[0].FormatId, Is.EqualTo("mjpeg"));
      Assert.That(specs[0].Recordable, Is.False);
    });
  }

  /// <summary>
  /// SCENARIO:
  /// An operator selects the main stream because the sub stream is a useless preview
  ///
  /// ACTION:
  /// Apply the source setting, then ask for derived streams
  ///
  /// EXPECTED RESULT:
  /// The declared stream is parented to the chosen stream rather than the lowest-resolution one
  /// </summary>
  [Test]
  public void GetDerivedStreams_SourceConfigured_UsesTheChosenStream()
  {
    var plugin = NewPlugin(out _, Stream("main", "h264", "1920x1080"), Stream("sub", "h264", "640x360"));

    var applied = plugin.ApplyValues(CameraId, new Dictionary<string, string> { ["sourceProfile"] = "main" });
    var specs = plugin.GetDerivedStreams(CameraId);

    Assert.That(applied.IsT0, Is.True);
    Assert.That(specs, Has.Count.EqualTo(1));
    Assert.That(specs[0].ParentProfile, Is.EqualTo("main"));
  }

  /// <summary>
  /// SCENARIO:
  /// A camera refresh removes the stream an operator had previously selected as the source
  ///
  /// ACTION:
  /// Configure "main", then re-resolve against a camera that only exposes "sub"
  ///
  /// EXPECTED RESULT:
  /// The stale selection is treated as unset and falls back to the computed default, and the
  /// stored value is left intact so the original choice is honoured if the stream returns
  /// </summary>
  [Test]
  public void GetDerivedStreams_ConfiguredSourceMissing_FallsBackWithoutOverwriting()
  {
    var plugin = NewPlugin(out var config, Stream("main", "h264", "1920x1080"), Stream("sub", "h264", "640x360"));
    plugin.ApplyValues(CameraId, new Dictionary<string, string> { ["sourceProfile"] = "main" });

    _registry.SetStreams(Stream("sub", "h264", "640x360"));
    var specs = plugin.GetDerivedStreams(CameraId);

    Assert.That(specs, Has.Count.EqualTo(1));
    Assert.That(specs[0].ParentProfile, Is.EqualTo("sub"));
    Assert.That(config.Get(ThumbnailPlugin.SourceKey(CameraId), ""), Is.EqualTo("main"));
  }

  /// <summary>
  /// SCENARIO:
  /// A camera exposes only streams the thumbnailer cannot decode
  ///
  /// ACTION:
  /// Ask for derived streams on a camera whose only stream is mjpeg
  ///
  /// EXPECTED RESULT:
  /// Nothing is declared, so the reconciler soft-deletes any existing thumbnail stream rather
  /// than leaving one that can never produce a frame
  /// </summary>
  [Test]
  public void GetDerivedStreams_UnsupportedCodec_DeclaresNothing()
  {
    var plugin = NewPlugin(out _, Stream("main", "mjpeg", "640x480"));

    Assert.That(plugin.GetDerivedStreams(CameraId), Is.Empty);
  }

  /// <summary>
  /// SCENARIO:
  /// Derived streams are only declared for root streams
  ///
  /// ACTION:
  /// Ask for derived streams on a camera whose only h264 stream is not a root stream
  ///
  /// EXPECTED RESULT:
  /// Nothing is declared, so the analyzer never parents a thumbnail to another derived stream
  /// </summary>
  [Test]
  public void GetDerivedStreams_NonRootStream_DeclaresNothing()
  {
    var plugin = NewPlugin(out _, Stream("derived", "h264", "640x360", isRoot: false));

    Assert.That(plugin.GetDerivedStreams(CameraId), Is.Empty);
  }

  /// <summary>
  /// SCENARIO:
  /// The settings UI offers only streams the thumbnailer can actually decode
  ///
  /// ACTION:
  /// Read the camera schema for a camera mixing h264, h265 and mjpeg streams
  ///
  /// EXPECTED RESULT:
  /// A single select field lists the two decodable streams and defaults to the smallest
  /// </summary>
  [Test]
  public void GetSchema_ListsDecodableStreamsOnly()
  {
    var plugin = NewPlugin(out _,
      Stream("main", "h264", "1920x1080"),
      Stream("sub", "h265", "640x360"),
      Stream("still", "mjpeg", "320x240"));

    var field = plugin.GetSchema(CameraId).Single().Fields.Single();

    Assert.Multiple(() =>
    {
      Assert.That(field.Type, Is.EqualTo("select"));
      Assert.That(field.Options!.Select(o => o.Value), Is.EqualTo(new[] { "main", "sub" }));
      Assert.That(field.DefaultValue, Is.EqualTo("sub"));
    });
  }

  /// <summary>
  /// SCENARIO:
  /// A source profile is submitted that the camera does not expose
  ///
  /// ACTION:
  /// Validate the unknown value
  ///
  /// EXPECTED RESULT:
  /// A BadRequest error is returned so the setting is rejected rather than stored and silently
  /// falling back forever
  /// </summary>
  [Test]
  public void ValidateValue_UnknownProfile_IsRejected()
  {
    var plugin = NewPlugin(out _, Stream("sub", "h264", "640x360"));

    var result = plugin.ValidateValue(CameraId, "sourceProfile", "nope");

    Assert.That(result.IsT1, Is.True);
    Assert.That(result.AsT1.Result, Is.EqualTo(Result.BadRequest));
  }

  /// <summary>
  /// SCENARIO:
  /// Plugin-wide settings carry the agreed defaults before anything is configured
  ///
  /// ACTION:
  /// Read the plugin settings values
  ///
  /// EXPECTED RESULT:
  /// A 240px bounding size at quality 70 with a zero interval, meaning every keyframe
  /// </summary>
  [Test]
  public void GetValues_BeforeConfiguration_ReportsDefaults()
  {
    var plugin = NewPlugin(out _, Stream("sub", "h264", "640x360"));

    var values = plugin.GetValues();

    Assert.Multiple(() =>
    {
      Assert.That(values["size"], Is.EqualTo("240"));
      Assert.That(values["quality"], Is.EqualTo("70"));
      Assert.That(values["interval"], Is.EqualTo("0"));
    });
  }

  /// <summary>
  /// SCENARIO:
  /// Out-of-range plugin settings are submitted
  ///
  /// ACTION:
  /// Validate a zero quality and an oversized bounding box
  ///
  /// EXPECTED RESULT:
  /// Both are rejected as BadRequest
  /// </summary>
  [Test]
  public void ValidateValue_OutOfRangeSettings_AreRejected()
  {
    var plugin = NewPlugin(out _, Stream("sub", "h264", "640x360"));

    Assert.Multiple(() =>
    {
      Assert.That(plugin.ValidateValue("quality", "0").IsT1, Is.True);
      Assert.That(plugin.ValidateValue("size", "4000").IsT1, Is.True);
      Assert.That(plugin.ValidateValue("interval", "-1").IsT1, Is.True);
      Assert.That(plugin.ValidateValue("size", "240").IsT0, Is.True);
    });
  }

  /// <summary>
  /// SCENARIO:
  /// The host probes StartStreamAsync at pipeline construct time and cancels the iteration
  /// immediately, which is how DerivedStreamPipeline discovers the derived stream's shape
  ///
  /// ACTION:
  /// Start a stream, then run its iteration through to cancellation
  ///
  /// EXPECTED RESULT:
  /// The parent tap is disposed when the iteration ends, so a probe leaves no demand raised on
  /// the parent pipeline and the shared source does not flap
  /// </summary>
  [Test]
  public async Task StartStreamAsync_IterationEnds_ReleasesParentTap()
  {
    var plugin = NewPlugin(out _, Stream("sub", "h264", "640x360"));
    _tap.Enabled = true;

    var started = await plugin.StartStreamAsync(CameraId, "sub", CancellationToken.None);
    Assert.That(started.IsT0, Is.True);

    using var cts = new CancellationTokenSource();
    var drain = Task.Run(async () =>
    {
      try
      {
        await foreach (var _ in started.AsT0.ReadAsync(cts.Token)) { }
      }
      catch (OperationCanceledException) { }
    });

    await cts.CancelAsync();
    await drain;

    Assert.That(_tap.Tapped!.Disposed, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// The host calls StartStreamAsync twice, as it does for the construct-time probe followed by
  /// the call on first demand
  ///
  /// ACTION:
  /// Start a stream twice without draining the first
  ///
  /// EXPECTED RESULT:
  /// The second call takes its own tap and leaves the first alone, so the parent's demand never
  /// drops to zero between the probe and the real read
  /// </summary>
  [Test]
  public async Task StartStreamAsync_CalledAgain_DoesNotReleaseTheEarlierTap()
  {
    var plugin = NewPlugin(out _, Stream("sub", "h264", "640x360"));
    _tap.Enabled = true;

    var first = await plugin.StartStreamAsync(CameraId, "sub", CancellationToken.None);
    var firstTap = _tap.Tapped!;

    var second = await plugin.StartStreamAsync(CameraId, "sub", CancellationToken.None);

    Assert.Multiple(() =>
    {
      Assert.That(first.IsT0, Is.True);
      Assert.That(second.IsT0, Is.True);
      Assert.That(_tap.TapCount, Is.EqualTo(2));
      Assert.That(firstTap.Disposed, Is.False);
    });
  }

  private static readonly Guid CameraId = Guid.Parse("11111111-2222-3333-4444-555555555555");

  private FakeCameraRegistry _registry = null!;
  private FakeStreamTap _tap = null!;

  private ThumbnailPlugin NewPlugin(out IConfig config, params StreamProfile[] streams)
  {
    _registry = new FakeCameraRegistry(streams);
    _tap = new FakeStreamTap();
    var store = new InMemoryConfig();
    config = store;

    var plugin = new ThumbnailPlugin();
    plugin.Initialize(new PluginContext
    {
      Config = store,
      Environment = new FakeEnvironment(),
      LoggerFactory = NullPluginLoggerFactory.Instance,
      CameraRegistry = _registry,
      StreamTap = _tap
    });
    return plugin;
  }

  private static StreamProfile Stream(string profile, string codec, string resolution, bool isRoot = true) =>
    new()
    {
      Id = Guid.NewGuid(),
      Profile = profile,
      Kind = StreamKind.Quality,
      FormatId = "fmp4",
      Codec = codec,
      Resolution = resolution,
      Uri = $"rtsp://camera/{profile}",
      IsRootStream = isRoot
    };

  private sealed class FakeCameraRegistry : ICameraRegistry
  {
    private List<StreamProfile> _streams;

    public FakeCameraRegistry(params StreamProfile[] streams)
    {
      _streams = [.. streams];
    }

    public void SetStreams(params StreamProfile[] streams) => _streams = [.. streams];

    public Task<OneOf<IReadOnlyList<CameraInfo>, Error>> GetCamerasAsync(CancellationToken ct) =>
      Task.FromResult(OneOf<IReadOnlyList<CameraInfo>, Error>.FromT0([Camera()]));

    public Task<OneOf<CameraInfo, Error>> GetCameraAsync(Guid cameraId, CancellationToken ct) =>
      Task.FromResult(cameraId == CameraId
        ? OneOf<CameraInfo, Error>.FromT0(Camera())
        : Error.Create(ModuleIds.PluginThumbnail, 0x00FF, Result.NotFound, "no such camera"));

    private CameraInfo Camera() => new()
    {
      Id = CameraId,
      Name = "Test Camera",
      Address = "192.168.1.100",
      ProviderId = "onvif",
      Streams = _streams,
      Capabilities = []
    };
  }

  private sealed class FakeStreamTap : IStreamTap
  {
    public bool Enabled { get; set; }
    public int TapCount { get; private set; }
    public TrackedTap? Tapped { get; private set; }

    public Task<OneOf<IDataStream, Error>> TapAsync(Guid cameraId, string profile, CancellationToken ct)
    {
      if (!Enabled)
        return Task.FromResult<OneOf<IDataStream, Error>>(
          Error.Create(ModuleIds.PluginThumbnail, 0x00FE, Result.Unavailable, "not tapped in tests"));

      TapCount++;
      Tapped = new TrackedTap();
      return Task.FromResult(OneOf<IDataStream, Error>.FromT0(Tapped));
    }
  }

  private sealed class TrackedTap : IDataStream<H264NalUnit>, IDisposable
  {
    private readonly Channel<H264NalUnit> _channel = Channel.CreateUnbounded<H264NalUnit>();

    public StreamInfo Info { get; } = new() { DataFormat = "h264" };
    public Type FrameType => typeof(H264NalUnit);
    public bool Disposed { get; private set; }

    public async IAsyncEnumerable<H264NalUnit> ReadAsync(
      [EnumeratorCancellation] CancellationToken ct)
    {
      await foreach (var unit in _channel.Reader.ReadAllAsync(ct))
        yield return unit;
    }

    public void Dispose() => Disposed = true;
  }

  private sealed class InMemoryConfig : IConfig
  {
    private readonly Dictionary<string, string> _store = [];

    public string Get(string key, string defaultValue) =>
      _store.TryGetValue(key, out var val) ? val : defaultValue;

    public void Set(string key, string value) => _store[key] = value;
  }

  private sealed class FakeEnvironment : IServerEnvironment
  {
    public string DataPath => "/tmp/test";
  }
}
