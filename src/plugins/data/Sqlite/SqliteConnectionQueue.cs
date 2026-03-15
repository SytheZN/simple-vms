using System.Threading.Channels;
using Microsoft.Data.Sqlite;

namespace Data.Sqlite;

public sealed class SqliteConnectionQueue
{
  private readonly Channel<Operation> _channel;
  private readonly Task _processingTask;

  public SqliteConnectionQueue(string connectionString)
  {
    _channel = Channel.CreateUnbounded<Operation>(new UnboundedChannelOptions
    {
      SingleReader = true
    });
    _processingTask = Task.Run(() => ProcessQueueAsync(connectionString));
  }

  public Task<T> ExecuteAsync<T>(Func<SqliteConnection, T> work, CancellationToken ct)
  {
    var op = new Operation<T>(work);
    if (!_channel.Writer.TryWrite(op))
      throw new InvalidOperationException("Connection queue is closed");
    ct.Register(() => op.TryCancel());
    return op.Task;
  }

  public Task ExecuteAsync(Action<SqliteConnection> work, CancellationToken ct)
  {
    return ExecuteAsync(conn => { work(conn); return 0; }, ct);
  }

  private async Task ProcessQueueAsync(string connectionString)
  {
    using var conn = new SqliteConnection(connectionString);
    conn.Open();

    await foreach (var op in _channel.Reader.ReadAllAsync())
    {
      op.Execute(conn);
    }
  }

  private abstract class Operation
  {
    public abstract void Execute(SqliteConnection conn);
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

    public override void Execute(SqliteConnection conn)
    {
      if (_tcs.Task.IsCompleted)
        return;

      try
      {
        var result = _work(conn);
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
