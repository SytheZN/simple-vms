namespace Shared.Models;

public interface IStreamFormat
{
  string FormatId { get; }
  string FileExtension { get; }
  Type InputType { get; }
  Type OutputType { get; }
  OneOf<IVideoStream, Error> CreatePipeline(IDataStream input, StreamInfo info);
  OneOf<ISegmentReader, Error> CreateReader(Stream input);
}

public interface ISegmentReader : IAsyncDisposable
{
  Task<OneOf<Success, Error>> SeekAsync(long byteOffset, CancellationToken ct);
  IAsyncEnumerable<IDataUnit> ReadAsync(CancellationToken ct);
}
