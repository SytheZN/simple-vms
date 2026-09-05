namespace Client.Core.Decoding.Diagnostics;

public readonly record struct PipelineStats(
  string Label,
  string Profile,
  long BufferUs,
  long PositionUs,
  int FetcherGops,
  int FetcherBytes,
  int DecoderGops,
  int DecoderFrames);
