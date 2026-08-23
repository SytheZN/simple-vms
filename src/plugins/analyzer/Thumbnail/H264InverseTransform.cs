// The transforms below are transcribed from IdctResAddPred_c, WelsLumaDcDequantIdct and
// WelsChromaDcIdct of cisco/openh264, which makes this file a derivative work:
// https://raw.githubusercontent.com/cisco/openh264/master/codec/decoder/core/src/decode_mb_aux.cpp
// https://raw.githubusercontent.com/cisco/openh264/master/codec/decoder/core/src/decode_slice.cpp
//
// \copy
//     Copyright (c)  2013, Cisco Systems
//     All rights reserved.
//
//     Redistribution and use in source and binary forms, with or without
//     modification, are permitted provided that the following conditions
//     are met:
//
//        * Redistributions of source code must retain the above copyright
//          notice, this list of conditions and the following disclaimer.
//
//        * Redistributions in binary form must reproduce the above copyright
//          notice, this list of conditions and the following disclaimer in
//          the documentation and/or other materials provided with the
//          distribution.
//
//     THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
//     "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
//     LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS
//     FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE
//     COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT,
//     INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING,
//     BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
//     LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
//     CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT
//     LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN
//     ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
//     POSSIBILITY OF SUCH DAMAGE.

using Shared.Models.Formats;

namespace Analyzer.Thumbnail;

internal static class H264InverseTransform
{
  /// <summary>
  /// What the transform hands back: the two edges a later block predicts from, at coded
  /// resolution, and the block's average over each output sample.
  /// </summary>
  internal readonly struct Workspace
  {
    public required int[] Bottom { get; init; }
    public required int[] Right { get; init; }
    public required int[] Cells { get; init; }

    /// <summary>Null in production. The passes are too short to separate from outside.</summary>
    public IReconstructionObserver? Observer { get; init; }
  }

  /// <summary>
  /// Each dimension's basis is scaled by two to clear the halves the butterfly's shifts stand for,
  /// so a two-dimensional result carries four times the scale and the residual's own rounding
  /// shift absorbs it.
  /// </summary>
  private const int Shift = 8;
  private const int Rounding = 1 << (Shift - 1);

  /// <summary>The shift the residual alone takes, which is all a cell needs.</summary>
  private const int CellShift = 6;
  private const int CellRounding = 1 << (CellShift - 1);

  /// <summary>
  /// Undoes what a flat scaling list contributes to <see cref="H264Dequant"/>, which carries every
  /// matrix in the same scaled form so that only one of these exists.
  /// </summary>
  private const int ScaleRounding = 1 << (H264Dequant.Shift - 1);

  /// <summary>
  /// The two-by-two chroma transform has no shift of its own, so its only one is the scale's - and
  /// the halving that pairs with a transform this small.
  /// </summary>
  private const int ChromaDirectShift = H264Dequant.Shift + 1;

  /// <summary>
  /// The 8x8 basis is scaled by eight rather than two, so a two-dimensional result carries
  /// sixty-four times the scale over the residual's own shift.
  /// </summary>
  private const int Shift8 = 12;
  private const int Rounding8 = 1 << (Shift8 - 1);

  /// <summary>
  /// A cell spans four samples along each edge of the block, which is four bits more again.
  /// </summary>
  private const int CellShift8 = 16;
  private const int CellRounding8 = 1 << (CellShift8 - 1);

  /// <summary>
  /// Turns decoded levels into the block's last row and last column at full resolution, and its
  /// average over the single output sample a 4x4 covers.
  ///
  /// Neither edge needs the block formed. Projecting the coefficients onto the last row once
  /// leaves a four-entry stage that a second pass turns into the row itself, and the last column is
  /// the same walk transposed - which is what makes the cost follow how many coefficients there
  /// are rather than how big the block is.
  ///
  /// The average needs even less. Every basis function but the first sums to zero over the block,
  /// so all of them cancel and the average is the direct coefficient alone.
  /// </summary>
  public static void Apply4x4(
    in Workspace work, ReadOnlySpan<ushort> occupied, ReadOnlySpan<int> levels,
    int direct, ReadOnlySpan<int> dequant)
  {
    var observer = work.Observer;
    observer?.Begin(ReconstructionPhase.Samples);

    var basis = H264ResidualTables.Basis4;
    Span<int> alongRow = stackalloc int[4];
    Span<int> downColumn = stackalloc int[4];

    // Every position a coefficient landed on, folded together. Each stage is indexed by a half of
    // it, so its highest occupied entry follows without comparing anything per coefficient.
    var reach = 0;

    if (direct != 0)
    {
      var edge = direct * basis[0][3];
      alongRow[0] = edge;
      downColumn[0] = edge;
    }

    for (var i = 0; i < occupied.Length; i++)
    {
      var at = occupied[i];
      var value = (levels[i] * dequant[at] + ScaleRounding) >> H264Dequant.Shift;
      var vertical = at >> 2;
      var horizontal = at & 3;

      alongRow[horizontal] += value * basis[vertical][3];
      downColumn[vertical] += value * basis[horizontal][3];
      reach |= at;

      if (at == 0) direct += value;
    }

    var bottom = work.Bottom;
    var right = work.Right;
    var span = Math.Max(reach & 3, reach >> 2) + 1;

    for (var n = 0; n < 4; n++)
    {
      var row = 0;
      var column = 0;
      for (var k = 0; k < span; k++)
      {
        row += alongRow[k] * basis[k][n];
        column += downColumn[k] * basis[k][n];
      }

      bottom[n] = (row + Rounding) >> Shift;
      right[n] = (column + Rounding) >> Shift;
    }

    work.Cells[0] = (direct + CellRounding) >> CellShift;
    observer?.End(ReconstructionPhase.Samples);
  }

  /// <summary>
  /// The 4x4 transform where the block covers the whole prediction, which is every Intra_4x4 block.
  /// Each edge sample is folded into the prediction as it is formed, so the residual never reaches
  /// the staging buffers the other kinds need to place theirs from.
  /// </summary>
  public static void Combine4x4(
    in H264Workspace work, ReadOnlySpan<ushort> occupied, ReadOnlySpan<int> levels,
    ReadOnlySpan<int> dequant)
  {
    var observer = work.Observer;
    observer?.Begin(ReconstructionPhase.Samples);

    var basis = H264ResidualTables.Basis4;
    Span<int> alongRow = stackalloc int[4];
    Span<int> downColumn = stackalloc int[4];

    var reach = 0;
    var direct = 0;

    for (var i = 0; i < occupied.Length; i++)
    {
      var at = occupied[i];
      var value = (levels[i] * dequant[at] + ScaleRounding) >> H264Dequant.Shift;
      var vertical = at >> 2;
      var horizontal = at & 3;

      alongRow[horizontal] += value * basis[vertical][3];
      downColumn[vertical] += value * basis[horizontal][3];
      reach |= at;

      if (at == 0) direct += value;
    }

    var bottom = work.Bottom;
    var right = work.Right;
    var span = Math.Max(reach & 3, reach >> 2) + 1;

    for (var n = 0; n < 4; n++)
    {
      var row = 0;
      var column = 0;
      for (var k = 0; k < span; k++)
      {
        row += alongRow[k] * basis[k][n];
        column += downColumn[k] * basis[k][n];
      }

      bottom[n] = H264Workspace.Combine(bottom[n], (row + Rounding) >> Shift);
      right[n] = H264Workspace.Combine(right[n], (column + Rounding) >> Shift);
    }

    work.Means[0] = H264Workspace.Combine(work.Means[0], (direct + CellRounding) >> CellShift);

    observer?.End(ReconstructionPhase.Samples);
  }

  /// <summary>
  /// The 8x8 transform, producing the same three things the 4x4 one does. Two differences follow
  /// from the size: an 8x8 spans four output samples rather than one, and its average over one of
  /// them is no longer the direct coefficient alone - a basis function that changes sign inside
  /// the block still averages to nothing over the whole of it, but not over a quarter of it.
  ///
  /// The scale also carries the octave of the quantiser rather than having it folded into the
  /// table, which is how the 8x8 scales are stated upstream.
  /// </summary>
  public static void Apply8x8(
    in Workspace work, ReadOnlySpan<ushort> occupied, ReadOnlySpan<int> levels,
    int qp, ReadOnlySpan<int> dequant)
  {
    var observer = work.Observer;
    observer?.Begin(ReconstructionPhase.Edge);

    var basis = H264ResidualTables.Basis8;
    var octave = qp / 6;
    var raise = qp >= 36;
    var shift = raise ? octave - 6 : 6 - octave;
    var rounding = raise ? 0 : 1 << (shift - 1);

    Span<int> alongRow = stackalloc int[8];
    Span<int> downColumn = stackalloc int[8];
    Span<int> cellEven = stackalloc int[8];
    Span<int> cellOdd = stackalloc int[8];

    var reach = 0;

    for (var i = 0; i < occupied.Length; i++)
    {
      var at = occupied[i];
      var scaled = levels[i] * dequant[at];
      var value = raise ? scaled << shift : (scaled + rounding) >> shift;

      var vertical = at >> 3;
      var horizontal = at & 7;

      alongRow[horizontal] += value * basis[vertical][7];
      downColumn[vertical] += value * basis[horizontal][7];
      reach |= at;

      // Which half of the block a frequency lands in differs only by the sign the odd ones take,
      // so keeping the two parities apart settles both halves from one accumulation.
      if ((vertical & 1) == 0)
        cellEven[horizontal] += value * Half[vertical];
      else
        cellOdd[horizontal] += value * Half[vertical];
    }

    var bottom = work.Bottom;
    var right = work.Right;
    var span = Math.Max(reach & 7, reach >> 3) + 1;

    for (var n = 0; n < 8; n++)
    {
      var row = 0;
      var column = 0;
      for (var k = 0; k < span; k++)
      {
        row += alongRow[k] * basis[k][n];
        column += downColumn[k] * basis[k][n];
      }

      bottom[n] = (row + Rounding8) >> Shift8;
      right[n] = (column + Rounding8) >> Shift8;
    }

    observer?.End(ReconstructionPhase.Edge);
    observer?.Begin(ReconstructionPhase.Cells);

    for (var cy = 0; cy < 2; cy++)
    {
      var even = 0;
      var odd = 0;
      for (var k = 0; k < span; k++)
      {
        var term = cy == 0 ? cellEven[k] + cellOdd[k] : cellEven[k] - cellOdd[k];
        if ((k & 1) == 0)
          even += term * Half[k];
        else
          odd += term * Half[k];
      }

      work.Cells[cy * 2] = (even + odd + CellRounding8) >> CellShift8;
      work.Cells[cy * 2 + 1] = (even - odd + CellRounding8) >> CellShift8;
    }

    observer?.End(ReconstructionPhase.Cells);
  }

  /// <summary>
  /// What each frequency contributes to the mean over half the block, which is the span of one
  /// output sample. Derived from the basis rather than stated, since three of the eight change
  /// sign twice within a half and so contribute nothing to it at all.
  /// </summary>
  private static readonly int[] Half = BuildHalf();

  private static int[] BuildHalf()
  {
    var half = new int[8];
    for (var k = 0; k < 8; k++)
      for (var n = 0; n < 4; n++)
        half[k] += H264ResidualTables.Basis8[k][n];

    return half;
  }

  /// <summary>
  /// The separate transform an Intra_16x16 macroblock's sixteen direct terms are coded through,
  /// leaving each 4x4 block the one coefficient it does not carry itself.
  /// </summary>
  public static void LumaDirect(Span<int> block, int scale)
  {
    Span<int> staged = stackalloc int[16];

    for (var i = 0; i < 4; i++)
    {
      var at = i * 4;
      var z0 = block[at] + block[at + 2];
      var z1 = block[at] - block[at + 2];
      var z2 = block[at + 1] - block[at + 3];
      var z3 = block[at + 1] + block[at + 3];

      staged[at] = z0 + z3;
      staged[at + 1] = z1 + z2;
      staged[at + 2] = z1 - z2;
      staged[at + 3] = z0 - z3;
    }

    for (var i = 0; i < 4; i++)
    {
      var z0 = staged[i] + staged[8 + i];
      var z1 = staged[i] - staged[8 + i];
      var z2 = staged[4 + i] - staged[12 + i];
      var z3 = staged[4 + i] + staged[12 + i];

      block[i] = ((z0 + z3) * scale + 32) >> 6;
      block[4 + i] = ((z1 + z2) * scale + 32) >> 6;
      block[8 + i] = ((z1 - z2) * scale + 32) >> 6;
      block[12 + i] = ((z0 - z3) * scale + 32) >> 6;
    }
  }

  /// <summary>The same for a chroma component's four direct terms.</summary>
  public static void ChromaDirect(Span<int> block, int scale)
  {
    var sum = block[0] + block[1];
    var difference = block[0] - block[1];
    var lowerSum = block[2] + block[3];
    var lowerDifference = block[2] - block[3];

    block[0] = ((sum + lowerSum) * scale) >> ChromaDirectShift;
    block[1] = ((difference + lowerDifference) * scale) >> ChromaDirectShift;
    block[2] = ((sum - lowerSum) * scale) >> ChromaDirectShift;
    block[3] = ((difference - lowerDifference) * scale) >> ChromaDirectShift;
  }
}
