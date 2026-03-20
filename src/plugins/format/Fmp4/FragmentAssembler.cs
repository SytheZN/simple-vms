using Shared.Models.Formats;

namespace Format.Fmp4;

public record KeyframeOffset
{
  public required ulong Timestamp { get; init; }
  public required long ByteOffset { get; init; }
}

public sealed class FragmentAssembler
{
  private readonly TimestampConverter _timestamps;
  private uint _sequenceNumber;
  private long _bytesWritten;

  public FragmentAssembler(TimestampConverter timestamps)
  {
    _timestamps = timestamps;
  }

  public long BytesWritten => _bytesWritten;

  public (Fmp4Fragment fragment, KeyframeOffset? keyframeOffset) Assemble(
    IReadOnlyList<ReadOnlyMemory<byte>> annexBNals,
    IReadOnlyList<SampleEntry> samples,
    ulong firstTimestamp,
    bool isKeyframe)
  {
    _sequenceNumber++;

    var baseDecodeTime = _timestamps.ToDecodeTime(firstTimestamp);
    var moofBytes = MoofBuilder.Build(_sequenceNumber, baseDecodeTime, samples);
    var mdatBytes = MdatBuilder.Build(annexBNals);

    var moofOffset = _bytesWritten;

    var fragmentData = new byte[moofBytes.Length + mdatBytes.Length];
    moofBytes.CopyTo(fragmentData, 0);
    mdatBytes.CopyTo(fragmentData, moofBytes.Length);

    _bytesWritten += fragmentData.Length;

    var fragment = new Fmp4Fragment
    {
      Data = fragmentData,
      Timestamp = firstTimestamp,
      IsSyncPoint = isKeyframe,
      IsHeader = false
    };

    KeyframeOffset? keyframe = isKeyframe
      ? new KeyframeOffset { Timestamp = firstTimestamp, ByteOffset = moofOffset }
      : null;

    return (fragment, keyframe);
  }

  public void AddHeaderBytes(int headerSize)
  {
    _bytesWritten += headerSize;
  }
}
