using Shared.Models;

namespace Storage.Filesystem;

public sealed partial class FilesystemPlugin : IStorageProvider
{
  public string ProviderId => "filesystem";

  public Task<ISegmentHandle> CreateSegmentAsync(SegmentMetadata metadata, CancellationToken ct)
  {
    var relativePath = BuildRelativePath(metadata);
    var fullPath = Path.Combine(_rootPath, relativePath);
    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
    var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read,
      bufferSize: 65536, FileOptions.SequentialScan);
    ISegmentHandle handle = new FilesystemSegmentHandle(relativePath, stream, fullPath);
    return Task.FromResult(handle);
  }

  public Task<Stream> OpenReadAsync(string segmentRef, CancellationToken ct)
  {
    var fullPath = Path.Combine(_rootPath, segmentRef);
    Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
      bufferSize: 65536, FileOptions.SequentialScan);
    return Task.FromResult(stream);
  }

  public Task PurgeAsync(IReadOnlyList<string> segmentRefs, CancellationToken ct)
  {
    foreach (var segmentRef in segmentRefs)
    {
      var fullPath = Path.Combine(_rootPath, segmentRef);
      if (File.Exists(fullPath))
      {
        File.Delete(fullPath);
        CleanEmptyParents(Path.GetDirectoryName(fullPath)!);
      }
    }
    return Task.CompletedTask;
  }

  public Task<StorageStats> GetStatsAsync(CancellationToken ct)
  {
    var driveInfo = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(_rootPath))!);
    long recordingBytes = 0;
    if (Directory.Exists(_rootPath))
    {
      foreach (var file in Directory.EnumerateFiles(_rootPath, "*", SearchOption.AllDirectories))
        recordingBytes += new FileInfo(file).Length;
    }
    return Task.FromResult(new StorageStats
    {
      TotalBytes = driveInfo.TotalSize,
      UsedBytes = driveInfo.TotalSize - driveInfo.AvailableFreeSpace,
      FreeBytes = driveInfo.AvailableFreeSpace,
      RecordingBytes = recordingBytes
    });
  }

  private static string BuildRelativePath(SegmentMetadata metadata)
  {
    var utc = DateTimeOffset.FromUnixTimeMilliseconds((long)(metadata.StartTime / 1000)).UtcDateTime;
    return Path.Combine(
      metadata.CameraId.ToString(),
      metadata.Profile,
      utc.Year.ToString("D4"),
      utc.Month.ToString("D2"),
      utc.Day.ToString("D2"),
      $"{metadata.StartTime}.mp4");
  }

  private void CleanEmptyParents(string directory)
  {
    var root = Path.GetFullPath(_rootPath);
    var current = Path.GetFullPath(directory);
    while (current.Length > root.Length)
    {
      if (Directory.EnumerateFileSystemEntries(current).Any())
        break;
      Directory.Delete(current);
      current = Path.GetDirectoryName(current)!;
    }
  }
}

internal sealed class FilesystemSegmentHandle : ISegmentHandle
{
  private readonly FileStream _stream;
  private readonly string _fullPath;
  private bool _finalized;

  public string SegmentRef { get; }
  public Stream Stream => _stream;

  public FilesystemSegmentHandle(string segmentRef, FileStream stream, string fullPath)
  {
    SegmentRef = segmentRef;
    _stream = stream;
    _fullPath = fullPath;
  }

  public async Task FinalizeAsync(CancellationToken ct)
  {
    await _stream.FlushAsync(ct);
    _finalized = true;
  }

  public async ValueTask DisposeAsync()
  {
    await _stream.DisposeAsync();
    if (!_finalized && File.Exists(_fullPath))
      File.Delete(_fullPath);
  }
}
