namespace Shared.Models;

public interface ICaptureSource
{
  string Protocol { get; }
  Task<OneOf<IStreamConnection, Error>> ConnectAsync(CameraConnectionInfo info, CancellationToken ct);
}

public interface IStreamConnection : IAsyncDisposable
{
  StreamInfo Info { get; }
  IDataStream DataStream { get; }
  Task Completed { get; }
}
