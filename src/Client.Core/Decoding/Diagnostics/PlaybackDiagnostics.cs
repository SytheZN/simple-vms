namespace Client.Core.Decoding.Diagnostics;

public sealed class PlaybackDiagnostics
{
  private readonly Func<PlaybackStats> _pull;
  private readonly DiagnosticsSettings _settings;

  public bool Enabled => _settings.ShowOverlay;
  public FrameTimingRecorder FrameTiming { get; } = new();

  public PlaybackDiagnostics(Func<PlaybackStats> pull, DiagnosticsSettings settings)
  {
    _pull = pull;
    _settings = settings;
  }

  public void RecordFrame() => FrameTiming.Record();

  public PlaybackStats Snapshot() => _pull();
}
