namespace Shared.Models;

public interface IStreamFormat
{
  string FormatId { get; }
  string FileExtension { get; }
  ISegmentWriter CreateWriter(Stream output, CodecInfo codec);
  ISegmentReader CreateReader(Stream input);
}

public interface ISegmentWriter : IAsyncDisposable
{
  Task WriteNalUnitAsync(NalUnit unit, CancellationToken ct);
  Task FinalizeAsync(CancellationToken ct);
  IReadOnlyList<KeyframeEntry> Keyframes { get; }
}

public interface ISegmentReader : IAsyncDisposable
{
  Task SeekToKeyframeAsync(long byteOffset, CancellationToken ct);
  IAsyncEnumerable<Fragment> ReadFragmentsAsync(CancellationToken ct);
}
