namespace Server.Logging;

public sealed class HybridLogger : ILogger
{
  private static readonly string[] LevelLabels =
    ["trac", "dbug", "info", "warn", "fail", "crit", "none"];

  private readonly string _category;
  private readonly RotatingFileWriter _writer;

  public HybridLogger(string category, RotatingFileWriter writer)
  {
    _category = category;
    _writer = writer;
  }

  public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

  public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

  public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
    Exception? exception, Func<TState, Exception?, string> formatter)
  {
    if (!IsEnabled(logLevel)) return;

    var level = LevelLabels[(int)logLevel];
    var line = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} [{level}] {_category}: {formatter(state, exception)}";
    _writer.WriteLine(line);

    if (exception != null)
      _writer.WriteLine($"  {exception}");
  }
}
