using Shared.Models;
using Shared.Models.Formats;

namespace Format.Fmp4;

public sealed class Fmp4SegmentReader : ISegmentReader
{
  private readonly Stream _stream;
  private readonly BoxReader _reader;

  public Fmp4SegmentReader(Stream stream)
  {
    _stream = stream;
    _reader = new BoxReader(stream);
  }

  public Task<OneOf<Success, Error>> SeekAsync(long byteOffset, CancellationToken ct)
  {
    _stream.Position = byteOffset;
    return Task.FromResult(OneOf<Success, Error>.FromT0(new Success()));
  }

  public async IAsyncEnumerable<IDataUnit> ReadAsync(
    [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
  {
    while (_reader.Position < _reader.Length)
    {
      ct.ThrowIfCancellationRequested();

      var fragment = ReadFragment();
      if (fragment == null)
        yield break;

      yield return fragment;
      await Task.CompletedTask;
    }
  }

  private Fmp4Fragment? ReadFragment()
  {
    var moofHeader = FindBox("moof");
    if (moofHeader == null)
      return null;

    var moofStart = moofHeader.DataOffset - moofHeader.HeaderSize;
    var moofEnd = moofStart + moofHeader.Size;

    ulong timestamp = 0;
    var isKeyframe = false;

    _reader.Position = moofHeader.DataOffset;
    while (_reader.Position < moofEnd)
    {
      var childHeader = _reader.ReadHeader();
      if (childHeader == null)
        break;

      if (childHeader.Type == "traf")
      {
        var trafEnd = childHeader.DataOffset - childHeader.HeaderSize + childHeader.Size;
        while (_reader.Position < trafEnd)
        {
          var trafChild = _reader.ReadHeader();
          if (trafChild == null)
            break;

          if (trafChild.Type == "tfdt")
          {
            var fields = _reader.ReadFullBoxFields();
            if (fields is { version: 1 })
              timestamp = _reader.ReadUInt64() ?? 0;
            else if (fields != null)
              timestamp = _reader.ReadUInt32() ?? 0;
          }
          else if (trafChild.Type == "trun")
          {
            var fields = _reader.ReadFullBoxFields();
            if (fields != null)
            {
              var sampleCount = _reader.ReadUInt32() ?? 0;
              var hasDataOffset = (fields.Value.flags & 0x000001) != 0;
              var hasFirstSampleFlags = (fields.Value.flags & 0x000004) != 0;
              var hasSampleFlags = (fields.Value.flags & 0x000400) != 0;

              if (hasDataOffset) _reader.ReadUInt32();

              if (hasFirstSampleFlags)
              {
                var firstFlags = _reader.ReadUInt32() ?? 0;
                isKeyframe = (firstFlags & 0x02000000) != 0;
              }
              else if (sampleCount > 0 && hasSampleFlags)
              {
                var hasDuration = (fields.Value.flags & 0x000100) != 0;
                var hasSize = (fields.Value.flags & 0x000200) != 0;
                var hasCtsOffset = (fields.Value.flags & 0x000800) != 0;
                if (hasDuration) _reader.ReadUInt32();
                if (hasSize) _reader.ReadUInt32();
                var sampleFlags = _reader.ReadUInt32() ?? 0;
                isKeyframe = (sampleFlags & 0x02000000) != 0;
              }
            }
          }

          _reader.Position = trafChild.DataOffset - trafChild.HeaderSize + trafChild.Size;
        }
      }

      _reader.Position = childHeader.DataOffset - childHeader.HeaderSize + childHeader.Size;
    }

    _reader.Position = moofEnd;
    var mdatHeader = _reader.ReadHeader();
    if (mdatHeader == null || mdatHeader.Type != "mdat")
      return null;

    var mdatEnd = mdatHeader.DataOffset - mdatHeader.HeaderSize + mdatHeader.Size;

    var totalSize = (int)(mdatEnd - moofStart);
    _reader.Position = moofStart;
    var data = _reader.ReadBytes(totalSize);
    if (data == null)
      return null;

    return new Fmp4Fragment
    {
      Data = data,
      Timestamp = timestamp,
      IsSyncPoint = isKeyframe,
      IsHeader = false
    };
  }

  private BoxHeader? FindBox(string type)
  {
    while (_reader.Position < _reader.Length)
    {
      var header = _reader.ReadHeader();
      if (header == null)
        return null;
      if (header.Type == type)
        return header;
      _reader.Position = header.DataOffset - header.HeaderSize + header.Size;
    }
    return null;
  }

  public ValueTask DisposeAsync()
  {
    _stream.Dispose();
    return ValueTask.CompletedTask;
  }
}
