namespace Client.Core.Decoding.Diagnostics;

public readonly record struct PlaybackStats(
  string BackendName,
  string RendererName,
  string State,
  string Mode,
  double Rate,
  double CatchupRate,
  long PositionUs,
  long BufferUs,
  int FetcherGops,
  int FetcherBytes,
  int DecodedGops,
  int DecodedFrames,
  bool Buffering);
