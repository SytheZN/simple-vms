namespace Shared.Models;

public interface IStreamFormat
{
  string FormatId { get; }
  string FileExtension { get; }
  Type InputType { get; }
  Type OutputType { get; }
  Task<OneOf<IMuxStream, Error>> CreatePipelineAsync(IDataStream input, StreamInfo info, CancellationToken ct);
  OneOf<ISegmentReader, Error> CreateReader(Stream input);
}
