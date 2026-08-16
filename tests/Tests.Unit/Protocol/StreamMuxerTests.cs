using System.Buffers.Binary;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Protocol;

namespace Tests.Unit.Protocol;

[TestFixture]
public class StreamMuxerTests
{
  /// <summary>
  /// SCENARIO:
  /// A muxer configured with odd starting ID (client mode)
  ///
  /// ACTION:
  /// Open two streams
  ///
  /// EXPECTED RESULT:
  /// First stream gets ID 1, second gets ID 3
  /// </summary>
  [Test]
  public async Task OpenStream_OddStart_AllocatesOddIds()
  {
    var transport = new MemoryStream();
    await using var muxer = new StreamMuxer(transport, NullLogger.Instance, 1);

    var (id1, _) = muxer.OpenStream(StreamTypes.ApiRequest);
    var (id2, _) = muxer.OpenStream(StreamTypes.ApiRequest);

    Assert.That(id1, Is.EqualTo(1u));
    Assert.That(id2, Is.EqualTo(3u));
  }

  /// <summary>
  /// SCENARIO:
  /// A muxer configured with even starting ID (server mode)
  ///
  /// ACTION:
  /// Open two streams
  ///
  /// EXPECTED RESULT:
  /// First stream gets ID 2, second gets ID 4
  /// </summary>
  [Test]
  public async Task OpenStream_EvenStart_AllocatesEvenIds()
  {
    var transport = new MemoryStream();
    await using var muxer = new StreamMuxer(transport, NullLogger.Instance, 2);

    var (id1, _) = muxer.OpenStream(StreamTypes.ApiRequest);
    var (id2, _) = muxer.OpenStream(StreamTypes.ApiRequest);

    Assert.That(id1, Is.EqualTo(2u));
    Assert.That(id2, Is.EqualTo(4u));
  }

  /// <summary>
  /// SCENARIO:
  /// A stream is opened with a specific stream type
  ///
  /// ACTION:
  /// Open a stream and read the bytes written to the transport
  ///
  /// EXPECTED RESULT:
  /// The mux header contains the stream ID, and the payload starts with
  /// the 2-byte stream type header
  /// </summary>
  [Test]
  public async Task OpenStream_WritesCorrectMuxHeaderWithStreamType()
  {
    var transport = new MemoryStream();
    await using var muxer = new StreamMuxer(transport, NullLogger.Instance, 1);

    muxer.OpenStream(StreamTypes.LiveSubscribe);

    transport.Position = 0;
    var header = new byte[MessageEnvelope.MuxHeaderSize];
    transport.Read(header);
    var (streamId, flags, payloadLength) = MessageEnvelope.ReadMuxHeader(header);

    Assert.That(streamId, Is.EqualTo(1u));
    Assert.That(flags, Is.EqualTo((ushort)0));
    Assert.That(payloadLength, Is.EqualTo(2));

    var payload = new byte[payloadLength];
    transport.Read(payload);
    var streamType = BinaryPrimitives.ReadUInt16LittleEndian(payload);
    Assert.That(streamType, Is.EqualTo(StreamTypes.LiveSubscribe));
  }

  /// <summary>
  /// SCENARIO:
  /// A message arrives on the transport for a known stream ID
  ///
  /// ACTION:
  /// Create a stream, write a message to the transport, run the read loop
  ///
  /// EXPECTED RESULT:
  /// The message is delivered to the channel reader for that stream ID
  /// </summary>
  [Test]
  public async Task ReadLoop_RoutesMessageToCorrectStream()
  {
    var pipe = new DuplexPipe();
    await using var muxer = new StreamMuxer(pipe.ClientStream, NullLogger.Instance, 1);

    var reader = muxer.GetOrCreateStream(5);

    var payload = new byte[] { 0xAA, 0xBB };
    var frame = new byte[MessageEnvelope.MuxHeaderSize + payload.Length];
    MessageEnvelope.WriteMuxHeader(frame, 5, 0, payload.Length);
    payload.CopyTo(frame.AsSpan(MessageEnvelope.MuxHeaderSize));
    await pipe.ServerStream.WriteAsync(frame);
    await pipe.ServerStream.FlushAsync();
    pipe.ServerStream.Close();

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
    var readTask = muxer.RunReadLoopAsync(cts.Token);

    var msg = await reader.ReadAsync(cts.Token);

    Assert.That(msg.Payload.ToArray(), Is.EqualTo(new byte[] { 0xAA, 0xBB }));

    try { await readTask; } catch (OperationCanceledException) { }
  }

  /// <summary>
  /// SCENARIO:
  /// A FIN message arrives for a stream
  ///
  /// ACTION:
  /// Create a stream, send a FIN frame, run the read loop
  ///
  /// EXPECTED RESULT:
  /// The channel for that stream is completed
  /// </summary>
  [Test]
  public async Task ReadLoop_FinFlag_CompletesChannel()
  {
    var pipe = new DuplexPipe();
    await using var muxer = new StreamMuxer(pipe.ClientStream, NullLogger.Instance, 1);

    var reader = muxer.GetOrCreateStream(7);

    var frame = new byte[MessageEnvelope.MuxHeaderSize];
    MessageEnvelope.WriteMuxHeader(frame, 7, MessageEnvelope.FlagFin, 0);
    await pipe.ServerStream.WriteAsync(frame);
    await pipe.ServerStream.FlushAsync();
    pipe.ServerStream.Close();

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
    var readTask = muxer.RunReadLoopAsync(cts.Token);

    var completed = false;
    try { await reader.ReadAsync(cts.Token); }
    catch (ChannelClosedException) { completed = true; }

    Assert.That(completed, Is.True);

    try { await readTask; } catch (OperationCanceledException) { }
  }

  /// <summary>
  /// SCENARIO:
  /// Multiple concurrent sends on the muxer
  ///
  /// ACTION:
  /// Send two messages concurrently from different tasks
  ///
  /// EXPECTED RESULT:
  /// Both messages are written without interleaving (each frame is intact)
  /// </summary>
  [Test]
  public async Task SendAsync_ConcurrentSends_NoInterleaving()
  {
    var transport = new MemoryStream();
    await using var muxer = new StreamMuxer(transport, NullLogger.Instance, 1);

    var payload1 = new byte[100];
    Array.Fill(payload1, (byte)0x11);
    var payload2 = new byte[100];
    Array.Fill(payload2, (byte)0x22);

    await Task.WhenAll(
      muxer.SendAsync(1, 0, payload1, CancellationToken.None),
      muxer.SendAsync(3, 0, payload2, CancellationToken.None));

    transport.Position = 0;
    var frames = new List<(uint StreamId, byte[] Payload)>();

    while (transport.Position < transport.Length)
    {
      var header = new byte[MessageEnvelope.MuxHeaderSize];
      var read = transport.Read(header);
      if (read < MessageEnvelope.MuxHeaderSize) break;

      var (streamId, _, payloadLength) = MessageEnvelope.ReadMuxHeader(header);
      var data = new byte[payloadLength];
      transport.Read(data);
      frames.Add((streamId, data));
    }

    Assert.That(frames, Has.Count.EqualTo(2));

    var frame1 = frames.First(f => f.StreamId == 1);
    var frame2 = frames.First(f => f.StreamId == 3);
    Assert.That(frame1.Payload, Is.All.EqualTo((byte)0x11));
    Assert.That(frame2.Payload, Is.All.EqualTo((byte)0x22));
  }

  /// <summary>
  /// SCENARIO:
  /// A stream has sent its full window without any credit returning
  ///
  /// ACTION:
  /// Send one more frame on that stream
  ///
  /// EXPECTED RESULT:
  /// The send does not complete, holding the sender to the window
  /// </summary>
  [Test]
  public async Task SendAsync_WindowExhausted_BlocksSender()
  {
    var transport = new MemoryStream();
    await using var muxer = new StreamMuxer(transport, NullLogger.Instance, 1);

    var (streamId, _) = muxer.OpenStream(StreamTypes.Playback);
    await muxer.SendAsync(streamId, 0, new byte[StreamMuxer.StreamWindowBytes - 1024],
      CancellationToken.None);

    var blocked = muxer.SendAsync(streamId, 0, new byte[4096], CancellationToken.None);
    var winner = await Task.WhenAny(blocked, Task.Delay(TimeSpan.FromMilliseconds(250)));

    Assert.That(winner, Is.Not.SameAs(blocked), "send should be held by flow control");
  }

  /// <summary>
  /// SCENARIO:
  /// A stream is blocked by flow control and the peer returns credit
  ///
  /// ACTION:
  /// Deliver a window update frame for that stream through the read loop
  ///
  /// EXPECTED RESULT:
  /// The blocked send completes
  /// </summary>
  [Test]
  public async Task WindowUpdate_ReleasesBlockedSender()
  {
    var transport = new LoopbackStream();
    await using var muxer = new StreamMuxer(transport, NullLogger.Instance, 1);

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var readLoop = muxer.RunReadLoopAsync(cts.Token);

    var (streamId, _) = muxer.OpenStream(StreamTypes.Playback);
    await muxer.SendAsync(streamId, 0, new byte[StreamMuxer.StreamWindowBytes - 1024],
      CancellationToken.None);

    var blocked = muxer.SendAsync(streamId, 0, new byte[4096], CancellationToken.None);

    var update = new byte[MessageEnvelope.MuxHeaderSize + MessageEnvelope.WindowUpdateSize];
    MessageEnvelope.WriteMuxHeader(
      update, streamId, MessageEnvelope.FlagWindowUpdate, MessageEnvelope.WindowUpdateSize);
    MessageEnvelope.WriteWindowUpdate(
      update.AsSpan(MessageEnvelope.MuxHeaderSize), StreamMuxer.StreamWindowBytes);
    transport.Deliver(update);

    var winner = await Task.WhenAny(blocked, Task.Delay(TimeSpan.FromSeconds(2)));
    Assert.That(winner, Is.SameAs(blocked), "returned credit should release the sender");
    await blocked;

    await cts.CancelAsync();
    try { await readLoop; } catch (OperationCanceledException) { }
  }

  /// <summary>
  /// SCENARIO:
  /// A single frame is larger than the whole flow control window
  ///
  /// ACTION:
  /// Send that frame on a freshly opened stream
  ///
  /// EXPECTED RESULT:
  /// The send completes rather than deadlocking against an unsatisfiable window
  /// </summary>
  [Test]
  public async Task SendAsync_FrameLargerThanWindow_DoesNotDeadlock()
  {
    var transport = new MemoryStream();
    await using var muxer = new StreamMuxer(transport, NullLogger.Instance, 1);

    var (streamId, _) = muxer.OpenStream(StreamTypes.Playback);
    var oversized = new byte[StreamMuxer.StreamWindowBytes + 4096];

    var send = muxer.SendAsync(streamId, 0, oversized, CancellationToken.None);
    var winner = await Task.WhenAny(send, Task.Delay(TimeSpan.FromSeconds(2)));

    Assert.That(winner, Is.SameAs(send), "oversized frame should not be blocked forever");
    await send;
  }

  private sealed class LoopbackStream : Stream
  {
    private readonly Channel<byte[]> _inbound = Channel.CreateUnbounded<byte[]>();
    private ReadOnlyMemory<byte> _current;

    public void Deliver(byte[] data) => _inbound.Writer.TryWrite(data);

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
      get => throw new NotSupportedException();
      set => throw new NotSupportedException();
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override int Read(byte[] buffer, int offset, int count) =>
      throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) { }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) =>
      ValueTask.CompletedTask;

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
      while (_current.IsEmpty)
        _current = await _inbound.Reader.ReadAsync(ct);

      var n = Math.Min(buffer.Length, _current.Length);
      _current[..n].CopyTo(buffer);
      _current = _current[n..];
      return n;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
      ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
  }

  private sealed class DuplexPipe
  {
    private readonly Pipe _pipe = new();

    public Stream ClientStream => _pipe.Reader;
    public Stream ServerStream => _pipe.Writer;

    private sealed class Pipe
    {
      private readonly MemoryStream _buffer = new();
      private readonly SemaphoreSlim _signal = new(0);
      private long _readPos;
      private bool _closed;

      public Stream Reader => new PipeReader(this);
      public Stream Writer => new PipeWriter(this);

      private void Write(ReadOnlySpan<byte> data)
      {
        lock (_buffer)
        {
          _buffer.Position = _buffer.Length;
          _buffer.Write(data);
          _signal.Release();
        }
      }

      private async Task<int> ReadAsync(Memory<byte> destination, CancellationToken ct)
      {
        while (true)
        {
          lock (_buffer)
          {
            var available = _buffer.Length - _readPos;
            if (available > 0)
            {
              var toRead = (int)Math.Min(destination.Length, available);
              _buffer.Position = _readPos;
              var read = _buffer.Read(destination.Span[..toRead]);
              _readPos += read;
              return read;
            }
            if (_closed) return 0;
          }
          await _signal.WaitAsync(ct);
        }
      }

      private void Close()
      {
        lock (_buffer)
        {
          _closed = true;
          _signal.Release();
        }
      }

      private sealed class PipeReader(Pipe pipe) : Stream
      {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) =>
          pipe.ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).GetAwaiter().GetResult();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
          new(pipe.ReadAsync(buffer, ct));
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
      }

      private sealed class PipeWriter(Pipe pipe) : Stream
      {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => pipe.Write(buffer.AsSpan(offset, count));
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
          pipe.Write(buffer.Span);
          return ValueTask.CompletedTask;
        }
        public new void Close() => pipe.Close();
      }
    }
  }
}
