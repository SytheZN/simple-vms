using Client.Core.Tunnel;
using Microsoft.Extensions.Logging;
using Shared.Protocol;

namespace Client.Core.Streaming;

public interface IVideoFeed : IAsyncDisposable
{
  Guid CameraId { get; }
  string Profile { get; }
  ReadOnlyMemory<byte> LastInit { get; }

  event Action<ReadOnlyMemory<byte>>? OnInit;
  event Action<GopMessage>? OnGop;
  event Action<StreamStatus>? OnStatus;
  event Action<GapStatus>? OnGap;
  event Action? OnCompleted;

  Task SendFetchAsync(ulong from, ulong to, CancellationToken ct);
}

public sealed class VideoFeed : IVideoFeed
{
  private readonly MuxStream _stream;
  private readonly ILogger _logger;
  private Task? _readLoop;
  private CancellationTokenSource? _cts;

  public Guid CameraId { get; }
  public string Profile { get; }
  public ReadOnlyMemory<byte> LastInit { get; private set; }

  public event Action<ReadOnlyMemory<byte>>? OnInit;
  public event Action<GopMessage>? OnGop;
  public event Action<StreamStatus>? OnStatus;
  public event Action<GapStatus>? OnGap;
  public event Action? OnCompleted;

  internal VideoFeed(MuxStream stream, Guid cameraId, string profile, ILogger logger)
  {
    _stream = stream;
    CameraId = cameraId;
    Profile = profile;
    _logger = logger;
  }

  public Task SendFetchAsync(ulong from, ulong to, CancellationToken ct)
  {
    var profileBytes = System.Text.Encoding.UTF8.GetBytes(Profile);
    var payload = new byte[2 + profileBytes.Length + 16];
    payload[0] = (byte)ClientMessageType.Fetch;
    payload[1] = (byte)profileBytes.Length;
    profileBytes.CopyTo(payload, 2);
    System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(
      payload.AsSpan(2 + profileBytes.Length), from);
    System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(
      payload.AsSpan(2 + profileBytes.Length + 8), to);
    return _stream.SendAsync(0, payload, ct);
  }

  internal void Start(CancellationToken ct)
  {
    _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    _readLoop = FailFast.Run(() => ReadLoopAsync(_cts.Token));
  }

  private async Task ReadLoopAsync(CancellationToken ct)
  {
    while (!ct.IsCancellationRequested)
    {
      MuxMessage msg;
      try { msg = await _stream.Reader.ReadAsync(ct); }
      catch (System.Threading.Channels.ChannelClosedException) { break; }

      if (msg.Payload.Length == 0)
        continue;

      var type = StreamMessageReader.ReadServerType(msg.Payload.Span);
      switch (type)
      {
        case ServerMessageType.Init:
        {
          var init = StreamMessageReader.ReadInit(msg.Payload.Span);
          LastInit = init.Data.ToArray();
          OnInit?.Invoke(LastInit);
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
    OnCompleted?.Invoke();
  }

  public async ValueTask DisposeAsync()
  {
    _cts?.Cancel();
    if (_readLoop != null)
      await _readLoop;
    _cts?.Dispose();
    await _stream.DisposeAsync();
  }
}
