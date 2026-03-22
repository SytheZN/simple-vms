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
    var moofBytes = MoofBuilder.Build(
      _sequenceNumber, firstTimestamp, samples,
      wallClockUs: isKeyframe ? wallClockUs : 0);
    var mdatBytes = MdatBuilder.Build(annexBNals);

    var moofOffset = _bytesWritten;

    var fragmentData = new byte[moofBytes.Length + mdatBytes.Length];
    moofBytes.CopyTo(fragmentData, 0);
    mdatBytes.CopyTo(fragmentData, moofBytes.Length);

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
