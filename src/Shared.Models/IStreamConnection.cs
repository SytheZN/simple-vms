namespace Shared.Models;

public interface IStreamConnection : IAsyncDisposable
{
  StreamInfo Info { get; }
  IDataStream DataStream { get; }
  Task Completed { get; }
}
