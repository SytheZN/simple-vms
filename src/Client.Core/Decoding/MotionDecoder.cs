using System.Buffers.Binary;
using System.IO.Compression;
using Microsoft.Extensions.Logging;

namespace Client.Core.Decoding;

public sealed class MotionDecoder : IChunkDecoder<MotionFrame>
{
  private const int HeaderSize = 22;
  private const int MaxChains = 64;

  private readonly ILogger _logger;
  private readonly DecodeController<MotionFrame> _controller;
  private readonly Dictionary<ulong, byte[]> _chains = [];
  private readonly List<ulong> _chainOrder = [];

  public MotionDecoder(ILogger logger, Fetcher fetcher)
  {
    _logger = logger;
    _controller = new DecodeController<MotionFrame>(fetcher, this);
  }

  public void SetTarget(ulong[] gopTimestamps) => _controller.SetTarget(gopTimestamps);

  public MotionFrame? GetFrame(long ts) => _controller.GetFrame(ts);

  public (int Gops, int Frames) Stats() => _controller.Stats();

  public void Flush()
  {
    _controller.Clear();
    _chains.Clear();
    _chainOrder.Clear();
  }

  public void Decode(ReadOnlyMemory<byte> data, ulong gopTimestamp)
  {
    var offset = 0;
    while (offset < data.Length)
    {
      var unit = data.Span[offset..];
      if (unit.Length < HeaderSize)
      {
        _logger.LogWarning("Motion gop has {Bytes} trailing bytes", unit.Length);
        return;
      }
      if (unit[0] != 'M' || unit[1] != 'G' || unit[2] != 'R' || unit[3] != 'D')
      {
        _logger.LogWarning("Motion gop has no MGRD magic at offset {Offset}", offset);
        return;
      }

      var flags = unit[5];
      var timestamp = (long)BinaryPrimitives.ReadUInt64LittleEndian(unit[6..]);
      var cols = BinaryPrimitives.ReadUInt16LittleEndian(unit[14..]);
      var rows = BinaryPrimitives.ReadUInt16LittleEndian(unit[16..]);
      var payloadLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(unit[18..]);
      if (unit.Length < HeaderSize + payloadLength)
      {
        _logger.LogWarning("Motion gop truncated: {Have} of {Need} bytes",
          unit.Length, HeaderSize + payloadLength);
        return;
      }

      var cells = Inflate(data.Slice(offset + HeaderSize, payloadLength));
      offset += HeaderSize + payloadLength;
      if (cells.Length != cols * rows)
      {
        _logger.LogWarning("Motion cell count mismatch: {Count} for {Cols}x{Rows}",
          cells.Length, cols, rows);
        continue;
      }

      var sync = (flags & 0x01) != 0;
      if (!sync && _chains.TryGetValue(gopTimestamp, out var baseFrame))
        for (var i = 0; i < cells.Length; i++)
          cells[i] ^= baseFrame[i];
      SetChain(gopTimestamp, cells);

      _controller.PushFrame(new MotionFrame(timestamp, cells, cols, rows, sync), gopTimestamp);
    }
  }

  public void Dispose(MotionFrame frame) { }

  private void SetChain(ulong gopTimestamp, byte[] cells)
  {
    _chainOrder.Remove(gopTimestamp);
    _chainOrder.Add(gopTimestamp);
    _chains[gopTimestamp] = cells;
    while (_chains.Count > MaxChains)
    {
      _chains.Remove(_chainOrder[0]);
      _chainOrder.RemoveAt(0);
    }
  }

  private static byte[] Inflate(ReadOnlyMemory<byte> compressed)
  {
    using var input = new MemoryStream(compressed.ToArray());
    using var deflate = new DeflateStream(input, CompressionMode.Decompress);
    using var output = new MemoryStream();
    deflate.CopyTo(output);
    return output.ToArray();
  }
}
