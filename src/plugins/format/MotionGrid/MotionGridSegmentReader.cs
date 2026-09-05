using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Shared.Models;
using Shared.Models.Formats;

namespace Format.MotionGrid;

public sealed class MotionGridSegmentReader : ISegmentReader
{
  private readonly Stream _stream;

  public MotionGridSegmentReader(Stream stream)
  {
    _stream = stream;
  }

  public Task<OneOf<Success, Error>> SeekAsync(long byteOffset, CancellationToken ct)
  {
    _stream.Position = byteOffset;
    return Task.FromResult(OneOf<Success, Error>.FromT0(new Success()));
  }

  public async IAsyncEnumerable<IDataUnit> ReadAsync(
    [EnumeratorCancellation] CancellationToken ct)
  {
    while (_stream.Position < _stream.Length)
    {
      ct.ThrowIfCancellationRequested();

      if (!await SyncToMagicAsync(ct))
        yield break;

      var header = new byte[MotionGridMuxer.HeaderSize - 4];
      var read = await _stream.ReadAsync(header.AsMemory(), ct);
      if (read != header.Length)
        yield break;

      var version = header[0];
      if (version != MotionGridMuxer.Version)
        continue;

      var flags = header[1];
      var timestamp = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(2));
      var width = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(10));
      var height = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(12));
      var payloadSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(14));

      var fragmentBytes = new byte[MotionGridMuxer.HeaderSize + payloadSize];
      MotionGridMuxer.Magic.CopyTo(fragmentBytes, 0);
      header.CopyTo(fragmentBytes, 4);

      var payloadRead = await _stream.ReadAsync(
        fragmentBytes.AsMemory(MotionGridMuxer.HeaderSize, payloadSize), ct);
      if (payloadRead != payloadSize)
        yield break;

      yield return new MotionGridFragment
      {
        Data = fragmentBytes,
        Timestamp = timestamp,
        IsSyncPoint = (flags & MotionGridMuxer.FlagSyncPoint) != 0
      };
    }
  }

  private async Task<bool> SyncToMagicAsync(CancellationToken ct)
  {
    var buffer = new byte[1];
    var matched = 0;
    while (await _stream.ReadAsync(buffer.AsMemory(), ct) == 1)
    {
      if (buffer[0] == MotionGridMuxer.Magic[matched])
      {
        matched++;
        if (matched == MotionGridMuxer.Magic.Length)
          return true;
      }
      else
      {
        matched = buffer[0] == MotionGridMuxer.Magic[0] ? 1 : 0;
      }
    }

    return false;
  }

  public ValueTask DisposeAsync()
  {
    _stream.Dispose();
    return ValueTask.CompletedTask;
  }
}
