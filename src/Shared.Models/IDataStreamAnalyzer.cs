namespace Shared.Models;

public interface IDataStreamAnalyzer
{
  string AnalyzerId { get; }
  IReadOnlyList<string> SupportedCodecs { get; }
}
