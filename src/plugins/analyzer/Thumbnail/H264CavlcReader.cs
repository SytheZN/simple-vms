using H264;

namespace Analyzer.Thumbnail;

internal sealed class H264CavlcReader(
  byte[] rbsp, int length, int bitOffset, IObserverHarness<ReconstructionPhase>? observer)
  : H264.CavlcReader(rbsp, length, bitOffset, observer)
{
  public Macroblock ReadHeader(
    bool transform8x8Allowed, Span<sbyte> modes,
    ReadOnlySpan<sbyte> leftModes, ReadOnlySpan<sbyte> aboveModes,
    bool leftAvailable, bool aboveAvailable)
  {
    var mbType = ReadExpGolomb();

    if (mbType == 25)
      return new Macroblock { Kind = MbKind.Pcm };

    if (mbType > 0)
    {
      var index = (int)(mbType - 1);
      return new Macroblock
      {
        Kind = MbKind.Intra16x16,
        CbpLuma = index >= 12 ? 15 : 0,
        CbpChroma = (index >> 2) % 3,
        Predicted16x16Mode = index & 3,
        ChromaPredMode = (int)ReadExpGolomb(),
      };
    }

    var transform8x8 = transform8x8Allowed && ReadFlag();
    ReadPredModes(
      modes, leftModes, aboveModes, leftAvailable, aboveAvailable, transform8x8 ? 4 : 1);

    var chromaMode = (int)ReadExpGolomb();
    var pattern = H264.CavlcTables.Intra4x4CbpTable[ReadExpGolomb()];

    return new Macroblock
    {
      Kind = transform8x8 ? MbKind.Intra8x8 : MbKind.Intra4x4,
      CbpLuma = pattern & 15,
      CbpChroma = pattern >> 4,
      ChromaPredMode = chromaMode,
      Transform8x8 = transform8x8,
    };
  }

  private void ReadPredModes(
    Span<sbyte> modes, ReadOnlySpan<sbyte> leftModes, ReadOnlySpan<sbyte> aboveModes,
    bool leftAvailable, bool aboveAvailable, int span)
  {
    for (var i = 0; i < 16; i += span)
    {
      var predicted = MacroblockReader.PredictedMode(
        i, modes, leftModes, aboveModes, leftAvailable, aboveAvailable);

      var mode = predicted;
      if (!ReadFlag())
      {
        var remainder = (int)Read(3);
        mode = (sbyte)(remainder < predicted ? remainder : remainder + 1);
      }

      for (var j = 0; j < span; j++)
        modes[i + j] = mode;
    }
  }
}
