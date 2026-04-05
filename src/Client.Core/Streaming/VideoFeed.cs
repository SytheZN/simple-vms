using Client.Core.Tunnel;
using Shared.Protocol;

namespace Client.Core.Streaming;

public sealed class VideoFeed : IAsyncDisposable
{
  private readonly MuxStream _stream;
  private Task? _readLoop;
  private CancellationTokenSource? _cts;

  public Guid CameraId { get; }
  public string Profile { get; }

  public event Action<ReadOnlyMemory<byte>>? OnInit;
  public event Action<GopMessage>? OnGop;
  public event Action<StreamStatus>? OnStatus;
  public event Action<GapStatus>? OnGap;
  public event Action? OnCompleted;

  internal VideoFeed(MuxStream stream, Guid cameraId, string profile)
  {
    _stream = stream;
    CameraId = cameraId;
    Profile = profile;
  }

  internal void Start(CancellationToken ct)
  {
    _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    _readLoop = ReadLoopAsync(_cts.Token);
  }

  private async Task ReadLoopAsync(CancellationToken ct)
  {
    try
    {
      while (!ct.IsCancellationRequested)
      {
        MuxMessage msg;
        try { msg = await _stream.Reader.ReadAsync(ct); }
        catch (System.Threading.Channels.ChannelClosedException) { break; }

        if (msg.Payload.Length == 0)
          continue;

        try
        {
          var type = StreamMessageReader.ReadServerType(msg.Payload.Span);
          switch (type)
          {
            case ServerMessageType.Init:
            {
              var init = StreamMessageReader.ReadInit(msg.Payload.Span);
              OnInit?.Invoke(init.Data);
              break;
            }
            case ServerMessageType.Gop:
            {
              var gop = StreamMessageReader.ReadGop(msg.Payload.Span);
              OnGop?.Invoke(gop);
              break;
            }
            case ServerMessageType.Status when msg.Payload.Length >= 2:
            {
              var status = (StreamStatus)msg.Payload.Span[1];
              if (status == StreamStatus.Gap)
              {
                var gap = StreamMessageReader.ReadGap(msg.Payload.Span);
                OnGap?.Invoke(gap);
              }
              else
              {
                OnStatus?.Invoke(status);
              }
              break;
            }
          }
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
          // Malformed message - skip and continue
        }
      }
    }
    catch (OperationCanceledException) { }
    finally
    {
      OnCompleted?.Invoke();
    }
  }

  public async ValueTask DisposeAsync()
  {
    _cts?.Cancel();
    if (_readLoop != null)
    {
      try { await _readLoop; }
      catch (OperationCanceledException) { }
    }
    _cts?.Dispose();
    await _stream.DisposeAsync();
  }
}
