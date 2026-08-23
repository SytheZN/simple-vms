namespace Analyzer.Thumbnail;

internal enum ReconstructionPhase
{
  /// <summary>
  /// A coded block's own syntax - what it is, how it predicts, and whether it carries coefficients -
  /// all of which is read before any reconstruction of it begins. Never nested: a walk that recurses
  /// closes its bracket before descending, since one observer holds one open interval per phase.
  /// </summary>
  Header,

  /// <summary>Reference gathering, reference smoothing, and the mode's own kernel. Siblings.</summary>
  Gather,
  Smooth,
  Predict,

  /// <summary>Residual parsing, split where residual_coding's own structure splits. Siblings.</summary>
  Last,
  Significance,
  Levels,
  Emit,

  /// <summary>Inverse transform, split by which output each pass produces. Siblings.</summary>
  Edge,
  Cells,

  /// <summary>The paths that work in the sample domain: transform skip, 4x4, and bypass.</summary>
  Samples,

  Write,

  /// <summary>Not a phase of the work. An observer may use it for whatever it costs to watch.</summary>
  Baseline,
}

/// <summary>
/// Phase boundaries within block reconstruction. The stages interleave thousands of times per
/// picture, so nothing outside the decoder can separate them; an observer is how a caller that
/// wants to measure them gets told where they fall. None is attached in production.
/// </summary>
internal interface IReconstructionObserver
{
  void Block(bool coded);
  void Begin(ReconstructionPhase phase);
  void End(ReconstructionPhase phase);
}
