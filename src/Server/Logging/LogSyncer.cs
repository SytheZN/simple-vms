namespace Server.Logging;

public sealed class LogSyncer : IDisposable
{
  private readonly string _tempDir;
  private readonly string _filePrefix;
  private readonly CancellationTokenSource _cts = new();
  private Task? _syncLoop;

  public LogSyncer(string tempDir, string filePrefix)
  {
    _tempDir = tempDir;
    _filePrefix = filePrefix;
  }

  public void Start(string dataLogDir, long startTime)
  {
    if (_syncLoop != null) return;
    _syncLoop = Task.Run(() => SyncLoopAsync(dataLogDir, startTime, _cts.Token));
  }

  private async Task SyncLoopAsync(string dataLogDir, long startTime, CancellationToken ct)
  {
    var previousRunsSynced = false;
    RotatingFileWriter? data = null;
    string? currentFile = null;
    long currentOffset = 0;

    while (!ct.IsCancellationRequested)
    {
      if (data == null)
      {
        try
        {
          data = new RotatingFileWriter(dataLogDir, startTime);
        }
        catch
        {
          await DelayOrStop(ct);
          continue;
        }
      }

      if (!previousRunsSynced)
      {
        try
        {
          SyncPreviousRuns(dataLogDir);
          previousRunsSynced = true;
        }
        catch
        {
          data.Dispose();
          data = null;
          await DelayOrStop(ct);
          continue;
        }
      }

      var tempFiles = RotatingFileWriter.ListLogFiles(_tempDir, _filePrefix);
      if (tempFiles.Count == 0)
      {
        await DelayOrStop(ct);
        continue;
      }

      if (currentFile != null && !tempFiles.Contains(currentFile))
      {
        data.WriteLine("--- gap: logs missing (temp logs rotated before sync) ---");
        currentFile = null;
        currentOffset = 0;
      }

      var startIndex = currentFile != null
        ? tempFiles.IndexOf(currentFile)
        : 0;

      if (startIndex < 0)
      {
        currentFile = null;
        currentOffset = 0;
        startIndex = 0;
      }

      var synced = false;
      try
      {
        for (var i = startIndex; i < tempFiles.Count; i++)
        {
          var file = tempFiles[i];
          var offset = file == currentFile ? currentOffset : 0;
          var newOffset = SyncFile(data, file, offset);

          if (newOffset > offset)
            synced = true;

          currentFile = file;
          currentOffset = newOffset;
        }
      }
      catch
      {
        data.Dispose();
        data = null;
      }

      if (!synced)
        await DelayOrStop(ct);
    }

    data?.Dispose();
  }

  private void SyncPreviousRuns(string dataLogDir)
  {
    foreach (var tempFile in RotatingFileWriter.ListLogFiles(_tempDir))
    {
      var fileName = Path.GetFileName(tempFile);
      if (fileName.StartsWith(_filePrefix))
        continue;

      var destPath = Path.Combine(dataLogDir, fileName);
      if (!File.Exists(destPath))
        File.Copy(tempFile, destPath);

      File.Delete(tempFile);
    }
  }

  private static long SyncFile(RotatingFileWriter data, string path, long startOffset)
  {
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    if (startOffset > 0)
      stream.Seek(startOffset, SeekOrigin.Begin);

    using var reader = new StreamReader(stream);
    while (reader.ReadLine() is { } line)
      data.WriteLine(line);

    return stream.Position;
  }

  private static async Task DelayOrStop(CancellationToken ct)
  {
    try { await Task.Delay(250, ct); }
    catch (OperationCanceledException) { }
  }

  public void Dispose()
  {
    _cts.Cancel();
    _syncLoop?.GetAwaiter().GetResult();
    _cts.Dispose();
  }
}
