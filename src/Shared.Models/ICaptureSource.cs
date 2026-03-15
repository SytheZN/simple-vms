namespace Shared.Models;

public interface ICaptureSource
{
  string Protocol { get; }
  Task<IStreamConnection> ConnectAsync(CameraConnectionInfo info, CancellationToken ct);
}

public interface IStreamConnection : IAsyncDisposable
{
  IAsyncEnumerable<NalUnit> ReadNalUnitsAsync(CancellationToken ct);
  StreamInfo Info { get; }
}
