using Shared.Models;
using Storage.Filesystem;

namespace Tests.Unit.Storage;

[TestFixture]
public class FilesystemStorageTests
{
  private string _tempDir = null!;
  private FilesystemPlugin _plugin = null!;

  [SetUp]
  public void SetUp()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), $"fs-storage-test-{Guid.NewGuid():N}");
    Directory.CreateDirectory(_tempDir);

    _plugin = new FilesystemPlugin();
    var config = new FakeConfig(new Dictionary<string, string> { ["path"] = _tempDir });
    _plugin.Initialize(new PluginContext
    {
      Config = config,
      Environment = new FakeEnvironment(_tempDir),
      LoggerFactory = NullPluginLoggerFactory.Instance
    });
  }

  [TearDown]
  public void TearDown()
  {
    if (Directory.Exists(_tempDir))
      Directory.Delete(_tempDir, recursive: true);
  }

  /// <summary>
  /// SCENARIO:
  /// A segment is created for a camera and profile
  ///
  /// ACTION:
  /// Call CreateSegmentAsync with known metadata
  ///
  /// EXPECTED RESULT:
  /// File is created at {root}/{cameraId}/{profile}/{year}/{month}/{day}/{startTime}.mp4
  /// </summary>
  [Test]
  public async Task CreateSegment_WritesFile_AtExpectedPath()
  {
    var cameraId = Guid.Parse("550e8400-e29b-41d4-a716-446655440000");
    var metadata = new SegmentMetadata
    {
      CameraId = cameraId,
      Profile = "main",
      StartTime = 1742558400000000,
      Codec = "h264"
    };

    await using var handle = await _plugin.CreateSegmentAsync(metadata, CancellationToken.None);
    await handle.Stream.WriteAsync(new byte[] { 1, 2, 3 });
    await handle.FinalizeAsync(CancellationToken.None);

    var utc = DateTimeOffset.FromUnixTimeMilliseconds(1742558400000000 / 1000).UtcDateTime;
    var expected = Path.Combine(_tempDir,
      cameraId.ToString(),
      "main",
      utc.Year.ToString("D4"),
      utc.Month.ToString("D2"),
      utc.Day.ToString("D2"),
      "1742558400000000.mp4");
    Assert.That(File.Exists(expected), Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// A segment is created and finalized
  ///
  /// ACTION:
  /// Write data, finalize, read back via file path
  ///
  /// EXPECTED RESULT:
  /// File exists with correct content
  /// </summary>
  [Test]
  public async Task CreateSegment_FinalizeAsync_PersistsFile()
  {
    var metadata = CreateMetadata();
    var data = new byte[] { 10, 20, 30, 40, 50 };

    string segmentRef;
    await using (var handle = await _plugin.CreateSegmentAsync(metadata, CancellationToken.None))
    {
      await handle.Stream.WriteAsync(data);
      await handle.FinalizeAsync(CancellationToken.None);
      segmentRef = handle.SegmentRef;
    }

    var fullPath = Path.Combine(_tempDir, segmentRef);
    Assert.That(File.ReadAllBytes(fullPath), Is.EqualTo(data));
  }

  /// <summary>
  /// SCENARIO:
  /// A segment handle is disposed without calling FinalizeAsync
  ///
  /// ACTION:
  /// Create segment, write data, dispose without finalize
  ///
  /// EXPECTED RESULT:
  /// The incomplete file is deleted
  /// </summary>
  [Test]
  public async Task CreateSegment_DisposeWithoutFinalize_CleansUpFile()
  {
    var metadata = CreateMetadata();
    string segmentRef;

    await using (var handle = await _plugin.CreateSegmentAsync(metadata, CancellationToken.None))
    {
      await handle.Stream.WriteAsync(new byte[] { 1, 2, 3 });
      segmentRef = handle.SegmentRef;
    }

    var fullPath = Path.Combine(_tempDir, segmentRef);
    Assert.That(File.Exists(fullPath), Is.False);
  }

  /// <summary>
  /// SCENARIO:
  /// A finalized segment is opened for reading
  ///
  /// ACTION:
  /// Create, finalize, then OpenReadAsync
  ///
  /// EXPECTED RESULT:
  /// Returned stream contains the written data
  /// </summary>
  [Test]
  public async Task OpenReadAsync_ReturnsFileContents()
  {
    var metadata = CreateMetadata();
    var data = new byte[] { 99, 98, 97 };

    string segmentRef;
    await using (var handle = await _plugin.CreateSegmentAsync(metadata, CancellationToken.None))
    {
      await handle.Stream.WriteAsync(data);
      await handle.FinalizeAsync(CancellationToken.None);
      segmentRef = handle.SegmentRef;
    }

    await using var readStream = await _plugin.OpenReadAsync(segmentRef, CancellationToken.None);
    var buffer = new byte[data.Length];
    var bytesRead = await readStream.ReadAsync(buffer);

    Assert.That(bytesRead, Is.EqualTo(data.Length));
    Assert.That(buffer, Is.EqualTo(data));
  }

  /// <summary>
  /// SCENARIO:
  /// OpenReadAsync is called with a segment ref that does not exist on disk
  ///
  /// ACTION:
  /// Call OpenReadAsync with a nonexistent ref
  ///
  /// EXPECTED RESULT:
  /// Throws FileNotFoundException
  /// </summary>
  [Test]
  public void OpenReadAsync_MissingFile_Throws()
  {
    var ex = Assert.CatchAsync<IOException>(async () =>
      await _plugin.OpenReadAsync("nonexistent/path/file.mp4", CancellationToken.None));
    Assert.That(ex, Is.Not.Null);
  }

  /// <summary>
  /// SCENARIO:
  /// Segments are purged
  ///
  /// ACTION:
  /// Create and finalize segments, then purge them
  ///
  /// EXPECTED RESULT:
  /// Files are deleted and empty parent directories are cleaned up
  /// </summary>
  [Test]
  public async Task PurgeAsync_DeletesFiles_AndCleansEmptyDirs()
  {
    var metadata = CreateMetadata();

    string segmentRef;
    await using (var handle = await _plugin.CreateSegmentAsync(metadata, CancellationToken.None))
    {
      await handle.Stream.WriteAsync(new byte[] { 1 });
      await handle.FinalizeAsync(CancellationToken.None);
      segmentRef = handle.SegmentRef;
    }

    await _plugin.PurgeAsync([segmentRef], CancellationToken.None);

    var fullPath = Path.Combine(_tempDir, segmentRef);
    Assert.That(File.Exists(fullPath), Is.False);

    var cameraDir = Path.Combine(_tempDir, metadata.CameraId.ToString());
    Assert.That(Directory.Exists(cameraDir), Is.False);
  }

  /// <summary>
  /// SCENARIO:
  /// GetStatsAsync is called on a directory with recordings
  ///
  /// ACTION:
  /// Create a segment, then call GetStatsAsync
  ///
  /// EXPECTED RESULT:
  /// Returns non-negative space figures and correct RecordingBytes
  /// </summary>
  [Test]
  public async Task GetStatsAsync_ReturnsSpaceInfo()
  {
    var metadata = CreateMetadata();
    var data = new byte[1024];

    await using (var handle = await _plugin.CreateSegmentAsync(metadata, CancellationToken.None))
    {
      await handle.Stream.WriteAsync(data);
      await handle.FinalizeAsync(CancellationToken.None);
    }

    var stats = await _plugin.GetStatsAsync(CancellationToken.None);

    Assert.That(stats.TotalBytes, Is.GreaterThan(0));
    Assert.That(stats.FreeBytes, Is.GreaterThan(0));
    Assert.That(stats.UsedBytes, Is.GreaterThan(0));
    Assert.That(stats.RecordingBytes, Is.EqualTo(1024));
  }

  private static SegmentMetadata CreateMetadata() => new()
  {
    CameraId = Guid.Parse("550e8400-e29b-41d4-a716-446655440000"),
    Profile = "main",
    StartTime = 1742558400000000,
    Codec = "h264"
  };

  private sealed class FakeConfig : IConfig
  {
    private readonly Dictionary<string, string> _values;
    public FakeConfig(Dictionary<string, string> values) => _values = values;
    public T Get<T>(string key, T defaultValue) =>
      _values.TryGetValue(key, out var val) ? (T)(object)val : defaultValue;
    public void Set<T>(string key, T value) =>
      _values[key] = value?.ToString() ?? "";
  }

  private sealed class FakeEnvironment : IServerEnvironment
  {
    public string DataPath { get; }
    public FakeEnvironment(string dataPath) => DataPath = dataPath;
  }
}
