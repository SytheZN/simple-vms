using System.Threading.Channels;
using Microsoft.Data.Sqlite;

namespace Data.Sqlite;

internal sealed class ConnectionQueue
{
  private readonly Channel<Operation> _channel = Channel.CreateUnbounded<Operation>(
    new UnboundedChannelOptions { SingleReader = true });

  private Func<Func<SqliteConnection, object?>, object?>? _executor;
  private Task? _processingTask;

  public void Start(Func<Func<SqliteConnection, object?>, object?> executor)
  {
    _executor = executor;
    _processingTask = Task.Run(ProcessQueueAsync);
  }

  public Task<T> ExecuteAsync<T>(Func<SqliteConnection, T> work, CancellationToken ct)
  {
    var op = new Operation<T>(work);
    if (!_channel.Writer.TryWrite(op))
      throw new InvalidOperationException("Connection queue is closed");
    ct.Register(() => op.TryCancel());
    return op.Task;
  }

  private async Task ProcessQueueAsync()
  {
    await foreach (var op in _channel.Reader.ReadAllAsync())
    {
      op.Execute(_executor!);
    }
  }

  private abstract class Operation
  {
    public abstract void Execute(Func<Func<SqliteConnection, object?>, object?> executor);
    public abstract void TryCancel();
  }

  private sealed class Operation<T> : Operation
  {
    private readonly Func<SqliteConnection, T> _work;
    private readonly TaskCompletionSource<T> _tcs = new();

    public Task<T> Task => _tcs.Task;

    public Operation(Func<SqliteConnection, T> work)
    {
      _work = work;
    }

    public override void Execute(Func<Func<SqliteConnection, object?>, object?> executor)
    {
      if (_tcs.Task.IsCompleted)
        return;

      try
      {
        var result = (T)executor(conn => _work(conn))!;
        _tcs.TrySetResult(result);
      }
      catch (Exception ex)
      {
        _tcs.TrySetException(ex);
      }
    }

    public override void TryCancel()
    {
      _tcs.TrySetCanceled();
    }
  }
}
