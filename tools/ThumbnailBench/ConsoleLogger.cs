using Microsoft.Extensions.Logging;

namespace ThumbnailBench;

internal sealed class ConsoleLogger : ILogger
{
  public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

  public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

  public void Log<TState>(
    LogLevel logLevel, EventId eventId, TState state, Exception? exception,
    Func<TState, Exception?, string> formatter)
  {
    if (!IsEnabled(logLevel)) return;
    Console.Error.WriteLine($"[{logLevel}] {formatter(state, exception)}");
    if (exception != null) Console.Error.WriteLine(exception);
  }
}
