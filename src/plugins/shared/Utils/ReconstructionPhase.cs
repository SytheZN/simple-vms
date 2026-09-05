namespace Utils;

public enum ReconstructionPhase
{
  Header,
  Sao,

  Gather,
  Smooth,
  Predict,

  Last,
  Significance,
  Levels,
  Emit,

  Edge,
  Cells,

  Samples,

  Write,

  Baseline,
}
