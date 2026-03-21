using System.Text;

namespace Server.Logging;

public sealed class RotatingFileWriter : IDisposable
{
  private readonly string _dir;
  private readonly long _startTime;
  private readonly long _maxBytes;
  private readonly int _maxFiles;
  private readonly Lock _lock = new();
  private StreamWriter? _writer;
  private long _currentBytes;
  private int _counter;

  public string FilePrefix => $"server.{_startTime}.";

  public RotatingFileWriter(string dir, long startTime, long maxBytes = 10 * 1024 * 1024, int maxFiles = 10)
  {
    _dir = dir;
    _startTime = startTime;
    _maxBytes = maxBytes;
    _maxFiles = maxFiles;
    Directory.CreateDirectory(dir);
    OpenNext();
  }

  public void WriteLine(string line)
  {
    lock (_lock)
    {
      if (_writer == null) return;
      var byteCount = Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length;
      if (_currentBytes + byteCount > _maxBytes)
      {
        _writer.Dispose();
        _counter++;
        Prune();
        OpenNext();
      }
      _writer!.WriteLine(line);
      _currentBytes += byteCount;
    }
  }

  private void OpenNext()
  {
    var path = Path.Combine(_dir, $"server.{_startTime}.{_counter}.log");
    _currentBytes = 0;
    _writer = new StreamWriter(path, append: false) { AutoFlush = true };
  }

  private void Prune()
  {
    var files = ListLogFiles(_dir);
    if (files.Count <= _maxFiles) return;
    foreach (var file in files.Take(files.Count - _maxFiles))
    {
      try { File.Delete(file); }
      catch { }
    }
  }

  public static List<string> ListLogFiles(string dir, string? prefix = null)
  {
    if (!Directory.Exists(dir)) return [];
    var pattern = prefix != null ? $"{prefix}*.log" : "server.*.log";
    var files = Directory.GetFiles(dir, pattern);
    Array.Sort(files, StringComparer.Ordinal);
    return [.. files];
  }

  public void Dispose()
  {
    lock (_lock)
    {
      _writer?.Dispose();
      _writer = null;
    }
  }
}
