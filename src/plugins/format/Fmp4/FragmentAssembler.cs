using Shared.Models;
using Shared.Models.Formats;

namespace Format.Fmp4;

public record KeyframeOffset
{
  public required ulong Timestamp { get; init; }
  public required long ByteOffset { get; init; }
}

public sealed class FragmentAssembler
{
  private readonly BoxWriter _moofWriter = new();
  private uint _sequenceNumber;
  private long _bytesWritten;

  public long BytesWritten => _bytesWritten;
  public uint Timescale { get; init; } = 90000;

  public (Fmp4Fragment fragment, KeyframeOffset? keyframeOffset) Assemble(
    IReadOnlyList<ReadOnlyMemory<byte>> annexBNals,
    IReadOnlyList<SampleEntry> samples,
    ulong firstTimestamp,
    bool isKeyframe)
  {
    _sequenceNumber++;

    var wallClockUs = DateTimeOffset.UtcNow.ToUnixMicroseconds();

    _moofWriter.Reset();
    var moofLen = MoofBuilder.WriteTo(
      _moofWriter, _sequenceNumber, firstTimestamp, samples,
      wallClockUs: isKeyframe ? wallClockUs : 0);

    var mdatPayloadSize = 0;
    foreach (var nal in annexBNals)
      mdatPayloadSize += NalConverter.LengthPrefixedSize(nal.Span);
    var mdatLen = 8 + mdatPayloadSize;

    var moofOffset = _bytesWritten;
    var fragmentData = new byte[moofLen + mdatLen];

    _moofWriter.WrittenSpan.CopyTo(fragmentData);

    var totalMdat = (uint)mdatLen;
    var mdatStart = moofLen;
    fragmentData[mdatStart] = (byte)(totalMdat >> 24);
    fragmentData[mdatStart + 1] = (byte)(totalMdat >> 16);
    fragmentData[mdatStart + 2] = (byte)(totalMdat >> 8);
    fragmentData[mdatStart + 3] = (byte)totalMdat;
    fragmentData[mdatStart + 4] = (byte)'m';
    fragmentData[mdatStart + 5] = (byte)'d';
    fragmentData[mdatStart + 6] = (byte)'a';
    fragmentData[mdatStart + 7] = (byte)'t';

    var offset = mdatStart + 8;
    foreach (var nal in annexBNals)
    {
      var written = NalConverter.WriteLengthPrefixed(nal.Span, fragmentData.AsSpan(offset));
      offset += written;
    }

    _bytesWritten += fragmentData.Length;

    var fragment = new Fmp4Fragment
    {
      Data = fragmentData,
      Timestamp = wallClockUs,
      MediaTimestamp = firstTimestamp,
      IsSyncPoint = isKeyframe,
      IsHeader = false
    };

    KeyframeOffset? keyframe = isKeyframe
      ? new KeyframeOffset { Timestamp = wallClockUs, ByteOffset = moofOffset }
      : null;

    return (fragment, keyframe);
  }

  public void AddHeaderBytes(int headerSize)
  {
    _bytesWritten += headerSize;
  }
}
