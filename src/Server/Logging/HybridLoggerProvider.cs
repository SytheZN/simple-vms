using System.Collections.Concurrent;

namespace Server.Logging;

public sealed class HybridLoggerProvider : ILoggerProvider, IDisposable
{
  private readonly long _startTime;
  private readonly string _tempDir;
  private readonly RotatingFileWriter _temp;
  private readonly LogSyncer _syncer;
  private readonly ConcurrentDictionary<string, HybridLogger> _loggers = new();

  public HybridLoggerProvider()
  {
    _startTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    _tempDir = Path.Combine(Path.GetTempPath(), "vms");
    _temp = new RotatingFileWriter(_tempDir, _startTime);
    _syncer = new LogSyncer(_tempDir, _temp.FilePrefix);
  }

  public void EnableDataDir(string dataPath)
  {
    var dataLogDir = Path.Combine(dataPath, "logs");
    _syncer.Start(dataLogDir, _startTime);
  }

  public ILogger CreateLogger(string categoryName) =>
    _loggers.GetOrAdd(categoryName, name => new HybridLogger(name, _temp));

  public void Dispose()
  {
    _syncer.Dispose();
    _temp.Dispose();
  }
}
