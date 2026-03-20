using System.Threading.Channels;
using Shared.Models;

namespace Capture.Rtsp;

public sealed class DataStream<T> : IDataStream<T> where T : IDataUnit
{
  private readonly Channel<T> _channel;

  public StreamInfo Info { get; }
  public Type FrameType => typeof(T);

  public DataStream(StreamInfo info, int capacity = 256)
  {
    Info = info;
    _channel = Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
    {
      FullMode = BoundedChannelFullMode.DropOldest,
      SingleReader = false,
      SingleWriter = true
    });
  }

  public ChannelWriter<T> Writer => _channel.Writer;

  public async IAsyncEnumerable<T> ReadAsync(
    [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
  {
    await foreach (var item in _channel.Reader.ReadAllAsync(ct))
      yield return item;
  }

  public void Complete() => _channel.Writer.TryComplete();
}
