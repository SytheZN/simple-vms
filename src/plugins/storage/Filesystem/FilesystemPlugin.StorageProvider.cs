using Shared.Models;

namespace Storage.Filesystem;

public sealed partial class FilesystemPlugin : IStorageProvider
{
  public string ProviderId => "filesystem";

  public Task<OneOf<ISegmentHandle, Error>> CreateSegmentAsync(SegmentMetadata metadata, CancellationToken ct)
  {
    var relativePath = BuildRelativePath(metadata);
    var fullPath = Path.Combine(_rootPath, relativePath);
    try
    {
      Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
      var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read,
        bufferSize: 65536, FileOptions.SequentialScan);
      return Task.FromResult<OneOf<ISegmentHandle, Error>>(
        new FilesystemSegmentHandle(relativePath, stream, fullPath));
    }
    catch (Exception ex) when (IsFileSystemFailure(ex))
    {
      return Task.FromResult<OneOf<ISegmentHandle, Error>>(new Error(Result.InternalError,
        new DebugTag(ModuleIds.PluginFilesystemStorage, 0x0021),
        $"Failed to create segment '{relativePath}': {ex.Message}"));
    }
  }

  public Task<OneOf<Stream, Error>> OpenReadAsync(string segmentRef, CancellationToken ct)
  {
    var fullPath = Path.Combine(_rootPath, segmentRef);
    try
    {
      return Task.FromResult<OneOf<Stream, Error>>(new FileStream(
        fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
        bufferSize: 65536, FileOptions.SequentialScan));
    }
    catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
    {
      return Task.FromResult<OneOf<Stream, Error>>(new Error(Result.NotFound,
        new DebugTag(ModuleIds.PluginFilesystemStorage, 0x0022),
        $"Segment '{segmentRef}' not found"));
    }
    catch (Exception ex) when (IsFileSystemFailure(ex))
    {
      return Task.FromResult<OneOf<Stream, Error>>(new Error(Result.InternalError,
        new DebugTag(ModuleIds.PluginFilesystemStorage, 0x0023),
        $"Failed to open segment '{segmentRef}': {ex.Message}"));
    }
  }

  public Task<OneOf<Success, Error>> PurgeAsync(IReadOnlyList<string> segmentRefs, CancellationToken ct)
  {
    try
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
      return Task.FromResult<OneOf<Success, Error>>(new Success());
    }
    catch (Exception ex) when (IsFileSystemFailure(ex))
    {
      return Task.FromResult<OneOf<Success, Error>>(new Error(Result.InternalError,
        new DebugTag(ModuleIds.PluginFilesystemStorage, 0x0024),
        $"Failed to purge segments: {ex.Message}"));
    }
  }

  public Task<OneOf<StorageStats, Error>> GetStatsAsync(CancellationToken ct)
  {
    try
    {
      var driveInfo = new DriveInfo(Path.GetFullPath(_rootPath));
      long recordingBytes = 0;
      if (Directory.Exists(_rootPath))
      {
        foreach (var file in Directory.EnumerateFiles(_rootPath, "*", SearchOption.AllDirectories))
          recordingBytes += new FileInfo(file).Length;
      }
      return Task.FromResult<OneOf<StorageStats, Error>>(new StorageStats
      {
        TotalBytes = driveInfo.TotalSize,
        UsedBytes = driveInfo.TotalSize - driveInfo.AvailableFreeSpace,
        FreeBytes = driveInfo.AvailableFreeSpace,
        RecordingBytes = recordingBytes
      });
    }
    catch (Exception ex) when (IsFileSystemFailure(ex))
    {
      return Task.FromResult<OneOf<StorageStats, Error>>(new Error(Result.InternalError,
        new DebugTag(ModuleIds.PluginFilesystemStorage, 0x0025),
        $"Failed to read storage stats for '{_rootPath}': {ex.Message}"));
    }
  }

  public Task<OneOf<long, Error>> GetFreeBytesAsync(CancellationToken ct)
  {
    try
    {
      return Task.FromResult<OneOf<long, Error>>(new DriveInfo(Path.GetFullPath(_rootPath)).AvailableFreeSpace);
    }
    catch (Exception ex) when (IsFileSystemFailure(ex))
    {
      return Task.FromResult<OneOf<long, Error>>(new Error(Result.InternalError,
        new DebugTag(ModuleIds.PluginFilesystemStorage, 0x0020),
        $"Failed to read free space for '{_rootPath}': {ex.Message}"));
    }
  }

  internal static bool IsFileSystemFailure(Exception ex) =>
    ex is IOException or UnauthorizedAccessException or ArgumentException;

  private static string BuildRelativePath(SegmentMetadata metadata)
  {
    var utc = DateTimeOffset.FromUnixTimeMilliseconds((long)(metadata.StartTime / 1000)).UtcDateTime;
    return Path.Combine(
      metadata.CameraId.ToString(),
      metadata.Profile,
      utc.Year.ToString("D4"),
      utc.Month.ToString("D2"),
      utc.Day.ToString("D2"),
      $"{metadata.StartTime}.{metadata.FileExtension}");
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

  public async Task<OneOf<Success, Error>> FinalizeAsync(CancellationToken ct)
  {
    try
    {
      await _stream.FlushAsync(ct);
      _finalized = true;
      return new Success();
    }
    catch (Exception ex) when (FilesystemPlugin.IsFileSystemFailure(ex))
    {
      return new Error(Result.InternalError,
        new DebugTag(ModuleIds.PluginFilesystemStorage, 0x0026),
        $"Failed to finalize segment '{SegmentRef}': {ex.Message}");
    }
  }

  public async ValueTask DisposeAsync()
  {
    await _stream.DisposeAsync();
    if (!_finalized && File.Exists(_fullPath))
      File.Delete(_fullPath);
  }
}
