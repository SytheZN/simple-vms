namespace Client.Core.Decoding.Diagnostics;

public readonly record struct PlaybackStats(
  string BackendName,
  string RendererName,
  string State,
  string Mode,
  double Rate,
  double CatchupRate,
  bool Buffering);
