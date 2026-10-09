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
      Codec = "h264",
      FileExtension = "mp4"
    };

    await using var handle = (await _plugin.CreateSegmentAsync(metadata, CancellationToken.None)).AsT0;
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
    await using (var handle = (await _plugin.CreateSegmentAsync(metadata, CancellationToken.None)).AsT0)
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

    await using (var handle = (await _plugin.CreateSegmentAsync(metadata, CancellationToken.None)).AsT0)
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
    await using (var handle = (await _plugin.CreateSegmentAsync(metadata, CancellationToken.None)).AsT0)
    {
      await handle.Stream.WriteAsync(data);
      await handle.FinalizeAsync(CancellationToken.None);
      segmentRef = handle.SegmentRef;
    }

    await using var readStream = (await _plugin.OpenReadAsync(segmentRef, CancellationToken.None)).AsT0;
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
  /// Returns a NotFound error
  /// </summary>
  [Test]
  public async Task OpenReadAsync_MissingFile_ReturnsNotFound()
  {
    var result = await _plugin.OpenReadAsync("nonexistent/path/file.mp4", CancellationToken.None);

    Assert.That(result.IsT1, Is.True);
    Assert.That(result.AsT1.Result, Is.EqualTo(Result.NotFound));
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
    await using (var handle = (await _plugin.CreateSegmentAsync(metadata, CancellationToken.None)).AsT0)
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

    await using (var handle = (await _plugin.CreateSegmentAsync(metadata, CancellationToken.None)).AsT0)
    {
      await handle.Stream.WriteAsync(data);
      await handle.FinalizeAsync(CancellationToken.None);
    }

    var stats = (await _plugin.GetStatsAsync(CancellationToken.None)).AsT0;

    Assert.That(stats.TotalBytes, Is.GreaterThan(0));
    Assert.That(stats.FreeBytes, Is.GreaterThan(0));
    Assert.That(stats.UsedBytes, Is.GreaterThan(0));
    Assert.That(stats.RecordingBytes, Is.EqualTo(1024));
  }

  /// <summary>
  /// SCENARIO:
  /// GetFreeBytesAsync is called on a mounted directory
  ///
  /// ACTION:
  /// Call GetFreeBytesAsync
  ///
  /// EXPECTED RESULT:
  /// Returns a positive free space figure without error
  /// </summary>
  [Test]
  public async Task GetFreeBytesAsync_ReturnsDriveFreeSpace()
  {
    var free = await _plugin.GetFreeBytesAsync(CancellationToken.None);

    Assert.That(free.IsT0, Is.True);
    Assert.That(free.AsT0, Is.GreaterThan(0));
  }

  /// <summary>
  /// SCENARIO:
  /// A segment is created twice with the same metadata, so the file already exists
  ///
  /// ACTION:
  /// Call CreateSegmentAsync a second time
  ///
  /// EXPECTED RESULT:
  /// Returns an InternalError instead of throwing
  /// </summary>
  [Test]
  public async Task CreateSegment_FileExists_ReturnsError()
  {
    var metadata = CreateMetadata();
    await using var first = (await _plugin.CreateSegmentAsync(metadata, CancellationToken.None)).AsT0;

    var second = await _plugin.CreateSegmentAsync(metadata, CancellationToken.None);

    Assert.That(second.IsT1, Is.True);
    Assert.That(second.AsT1.Result, Is.EqualTo(Result.InternalError));
  }

  /// <summary>
  /// SCENARIO:
  /// The storage root is not writable
  ///
  /// ACTION:
  /// Call CreateSegmentAsync
  ///
  /// EXPECTED RESULT:
  /// Returns an InternalError instead of throwing
  /// </summary>
  [Test]
  [Platform("Unix")]
  [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
  public async Task CreateSegment_UnwritableRoot_ReturnsError()
  {
    File.SetUnixFileMode(_tempDir, UnixFileMode.UserRead | UnixFileMode.UserExecute);
    try
    {
      IgnoreUnlessPermissionsEnforced(_tempDir);

      var result = await _plugin.CreateSegmentAsync(CreateMetadata(), CancellationToken.None);

      Assert.That(result.IsT1, Is.True);
      Assert.That(result.AsT1.Result, Is.EqualTo(Result.InternalError));
    }
    finally
    {
      File.SetUnixFileMode(_tempDir, FullAccess);
    }
  }

  /// <summary>
  /// SCENARIO:
  /// A segment's directory is not writable, so its file cannot be deleted
  ///
  /// ACTION:
  /// Call PurgeAsync for the segment
  ///
  /// EXPECTED RESULT:
  /// Returns an InternalError instead of throwing
  /// </summary>
  [Test]
  [Platform("Unix")]
  [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
  public async Task PurgeAsync_UnwritableDirectory_ReturnsError()
  {
    string segmentRef;
    await using (var handle = (await _plugin.CreateSegmentAsync(CreateMetadata(), CancellationToken.None)).AsT0)
    {
      await handle.FinalizeAsync(CancellationToken.None);
      segmentRef = handle.SegmentRef;
    }

    var directory = Path.GetDirectoryName(Path.Combine(_tempDir, segmentRef))!;
    File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
    try
    {
      IgnoreUnlessPermissionsEnforced(directory);

      var result = await _plugin.PurgeAsync([segmentRef], CancellationToken.None);

      Assert.That(result.IsT1, Is.True);
      Assert.That(result.AsT1.Result, Is.EqualTo(Result.InternalError));
    }
    finally
    {
      File.SetUnixFileMode(directory, FullAccess);
    }
  }

  /// <summary>
  /// SCENARIO:
  /// A segment file exists but cannot be read
  ///
  /// ACTION:
  /// Call OpenReadAsync
  ///
  /// EXPECTED RESULT:
  /// Returns an InternalError instead of throwing
  /// </summary>
  [Test]
  [Platform("Unix")]
  [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
  public async Task OpenReadAsync_UnreadableFile_ReturnsError()
  {
    string segmentRef;
    await using (var handle = (await _plugin.CreateSegmentAsync(CreateMetadata(), CancellationToken.None)).AsT0)
    {
      await handle.FinalizeAsync(CancellationToken.None);
      segmentRef = handle.SegmentRef;
    }

    var fullPath = Path.Combine(_tempDir, segmentRef);
    File.SetUnixFileMode(fullPath, UnixFileMode.None);
    try
    {
      if (CanOpen(fullPath))
        Assert.Ignore("File modes are not enforced for this user");

      var result = await _plugin.OpenReadAsync(segmentRef, CancellationToken.None);

      Assert.That(result.IsT1, Is.True);
      Assert.That(result.AsT1.Result, Is.EqualTo(Result.InternalError));
    }
    finally
    {
      File.SetUnixFileMode(fullPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
  }

  private const UnixFileMode FullAccess =
    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

  private static void IgnoreUnlessPermissionsEnforced(string directory)
  {
    var probe = Path.Combine(directory, "probe");
    try
    {
      File.WriteAllText(probe, "");
      File.Delete(probe);
      Assert.Ignore("File modes are not enforced for this user");
    }
    catch (UnauthorizedAccessException)
    {
    }
  }

  private static bool CanOpen(string path)
  {
    try
    {
      using var _ = File.OpenRead(path);
      return true;
    }
    catch (UnauthorizedAccessException)
    {
      return false;
    }
  }

  private static SegmentMetadata CreateMetadata() => new()
  {
    CameraId = Guid.Parse("550e8400-e29b-41d4-a716-446655440000"),
    Profile = "main",
    StartTime = 1742558400000000,
    Codec = "h264",
    FileExtension = "mp4"
  };

  private sealed class FakeConfig : IConfig
  {
    private readonly Dictionary<string, string> _values;
    public FakeConfig(Dictionary<string, string> values) => _values = values;
    public string Get(string key, string defaultValue) =>
      _values.TryGetValue(key, out var val) ? val : defaultValue;
    public void Set(string key, string value) =>
      _values[key] = value;
  }

  private sealed class FakeEnvironment : IServerEnvironment
  {
    public string DataPath { get; }
    public FakeEnvironment(string dataPath) => DataPath = dataPath;
  }
}
