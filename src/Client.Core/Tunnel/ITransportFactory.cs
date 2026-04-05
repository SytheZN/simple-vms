using Client.Core.Platform;

namespace Client.Core.Tunnel;

public interface ITransportFactory
{
  Task<TransportConnection> ConnectAsync(
    string address, CredentialData creds, CancellationToken ct);
}

public sealed class TransportConnection : IAsyncDisposable, IDisposable
{
  public Stream Stream { get; }
  private readonly IDisposable[] _disposables;

  public TransportConnection(Stream stream, params IDisposable[] disposables)
  {
    Stream = stream;
    _disposables = disposables;
  }

  public async ValueTask DisposeAsync()
  {
    await Stream.DisposeAsync();
    foreach (var d in _disposables)
      d.Dispose();
  }

  public void Dispose()
  {
    Stream.Dispose();
    foreach (var d in _disposables)
      d.Dispose();
  }
}
