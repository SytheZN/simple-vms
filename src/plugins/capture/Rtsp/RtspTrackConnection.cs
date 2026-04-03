using Shared.Models;

namespace Capture.Rtsp;

internal sealed class RtspTrackConnection : IStreamConnection
{
  private readonly RtspSession _session;
  private bool _disposed;

  public StreamInfo Info { get; }
  public IDataStream DataStream { get; }
  public Task Completed => _session.Completed;

  public RtspTrackConnection(RtspSession session, RtspSession.TrackRegistration track)
  {
    _session = session;
    Info = track.Info;
    DataStream = track.DataStream;
  }

  public async ValueTask DisposeAsync()
  {
    if (_disposed) return;
    _disposed = true;
    await _session.RemoveDemandAsync();
  }
}
