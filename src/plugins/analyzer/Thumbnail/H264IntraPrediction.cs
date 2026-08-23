// The prediction modes below are transcribed from the WelsI4x4LumaPred*, WelsI8x8LumaPred*,
// WelsI16x16LumaPred* and WelsIChromaPred* functions of cisco/openh264, which makes this file a
// derivative work:
// https://raw.githubusercontent.com/cisco/openh264/master/codec/decoder/core/src/get_intra_predictor.cpp
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

internal static class H264IntraPrediction
{
  private const int Neutral = 128;

  /// <summary>
  /// Turns a mode's list into the only three things anything downstream reads: the row a later
  /// block predicts from, the column it predicts from, and the block's average. The block itself is
  /// never formed - each of its sixteen samples is one entry of the list, so the average is a
  /// weighted sum over the list and the two edges are windows into it.
  /// </summary>
  private static void Window(in H264Workspace work, ReadOnlySpan<byte> list, int mode)
  {
    var rows = H264ResidualTables.Intra4x4Windows[mode];
    var bottom = work.Bottom;
    var right = work.Right;
    var total = 0;

    for (var r = 0; r < 4; r++)
    {
      var at = rows[r];
      right[r] = list[at + 3];
      total += list[at] + list[at + 1] + list[at + 2] + list[at + 3];
    }

    var last = rows[3];
    for (var c = 0; c < 4; c++)
      bottom[c] = list[last + c];

    work.Means[0] = (byte)((total + 8) >> 4);
  }

  private static void Flat(in H264Workspace work, int size, int cells, byte value)
  {
    for (var i = 0; i < size; i++)
    {
      work.Bottom[i] = value;
      work.Right[i] = value;
    }

    for (var i = 0; i < cells * cells; i++)
      work.Means[i] = value;
  }

  /// <summary>
  /// The nine 4x4 modes, producing only the two edges and the block average.
  ///
  /// Losing the above-right neighbour needs no separate mode here. The gather repeats the row's
  /// last sample into it, and every formula that reads it then collapses to exactly the variant
  /// upstream spells out by hand - so DDL covers DDLTop and VL covers VLTop.
  /// </summary>
  public static void Predict4x4(in H264Workspace work, int mode, H264Neighbours found)
  {
    var observer = work.Observer;
    observer?.Begin(ReconstructionPhase.Predict);

    var references = work.References;

    if (mode == 1)
      Horizontal4x4(work, references);
    else if (mode == 2)
      Flat(work, 4, 1, Dc4x4(references, found));
    else
    {
      Span<byte> list = stackalloc byte[10];
      Build4x4(list, references, mode);
      Window(work, list, mode);
    }

    observer?.End(ReconstructionPhase.Predict);
  }

  private static void Horizontal4x4(in H264Workspace work, ReadOnlySpan<byte> references)
  {
    var total = 0;
    for (var i = 0; i < 4; i++)
    {
      var value = references[H264Workspace.Left(4, i)];
      work.Right[i] = value;
      total += value;
    }

    var last = work.Right[3];
    for (var c = 0; c < 4; c++)
      work.Bottom[c] = last;

    work.Means[0] = (byte)((total + 2) >> 2);
  }

  private static byte Dc4x4(ReadOnlySpan<byte> references, H264Neighbours found)
  {
    var left = 0;
    var top = 0;
    for (var i = 0; i < 4; i++)
    {
      left += references[H264Workspace.Left(4, i)];
      top += references[H264Workspace.Above(i)];
    }

    var hasLeft = (found & H264Neighbours.Left) != 0;
    var hasTop = (found & H264Neighbours.Top) != 0;

    if (hasLeft && hasTop) return (byte)((left + top + 4) >> 3);
    if (hasLeft) return (byte)((left + 2) >> 2);
    if (hasTop) return (byte)((top + 2) >> 2);
    return Neutral;
  }

  private static void Build4x4(Span<byte> list, ReadOnlySpan<byte> references, int mode)
  {
    int t0 = references[H264Workspace.Above(0)];
    int t1 = references[H264Workspace.Above(1)];
    int t2 = references[H264Workspace.Above(2)];
    int t3 = references[H264Workspace.Above(3)];

    if (mode == 0)
    {
      list[0] = (byte)t0;
      list[1] = (byte)t1;
      list[2] = (byte)t2;
      list[3] = (byte)t3;
      return;
    }

    int lt = references[H264Workspace.Corner];
    int l0 = references[H264Workspace.Left(4, 0)];
    int l1 = references[H264Workspace.Left(4, 1)];
    int l2 = references[H264Workspace.Left(4, 2)];
    int l3 = references[H264Workspace.Left(4, 3)];

    var topLeft = 1 + lt + l0;
    var leftTop = 1 + lt + t0;
    var top01 = 1 + t0 + t1;
    var top12 = 1 + t1 + t2;
    var top23 = 1 + t2 + t3;
    var left01 = 1 + l0 + l1;
    var left12 = 1 + l1 + l2;
    var left23 = 1 + l2 + l3;

    switch (mode)
    {
      case 3:
      {
        int t4 = references[H264Workspace.Above(4)];
        int t5 = references[H264Workspace.Above(5)];
        int t6 = references[H264Workspace.Above(6)];
        int t7 = references[H264Workspace.Above(7)];

        list[0] = (byte)((2 + t0 + t2 + (t1 << 1)) >> 2);
        list[1] = (byte)((2 + t1 + t3 + (t2 << 1)) >> 2);
        list[2] = (byte)((2 + t2 + t4 + (t3 << 1)) >> 2);
        list[3] = (byte)((2 + t3 + t5 + (t4 << 1)) >> 2);
        list[4] = (byte)((2 + t4 + t6 + (t5 << 1)) >> 2);
        list[5] = (byte)((2 + t5 + t7 + (t6 << 1)) >> 2);
        list[6] = (byte)((2 + t6 + t7 + (t7 << 1)) >> 2);
        break;
      }

      case 4:
        list[0] = (byte)((left12 + left23) >> 2);
        list[1] = (byte)((left01 + left12) >> 2);
        list[2] = (byte)((topLeft + left01) >> 2);
        list[3] = (byte)((topLeft + leftTop) >> 2);
        list[4] = (byte)((leftTop + top01) >> 2);
        list[5] = (byte)((top01 + top12) >> 2);
        list[6] = (byte)((top12 + top23) >> 2);
        break;

      case 5:
        list[0] = (byte)((2 + lt + (l0 << 1) + l1) >> 2);
        list[1] = (byte)(leftTop >> 1);
        list[2] = (byte)(top01 >> 1);
        list[3] = (byte)(top12 >> 1);
        list[4] = (byte)(top23 >> 1);
        list[5] = (byte)((2 + l0 + (l1 << 1) + l2) >> 2);
        list[6] = (byte)((2 + l0 + (lt << 1) + t0) >> 2);
        list[7] = (byte)((2 + lt + (t0 << 1) + t1) >> 2);
        list[8] = (byte)((2 + t0 + (t1 << 1) + t2) >> 2);
        list[9] = (byte)((2 + t1 + (t2 << 1) + t3) >> 2);
        break;

      case 6:
        list[0] = (byte)(left23 >> 1);
        list[1] = (byte)((left12 + left23) >> 2);
        list[2] = (byte)(left12 >> 1);
        list[3] = (byte)((left01 + left12) >> 2);
        list[4] = (byte)(left01 >> 1);
        list[5] = (byte)((topLeft + left01) >> 2);
        list[6] = (byte)(topLeft >> 1);
        list[7] = (byte)((topLeft + leftTop) >> 2);
        list[8] = (byte)((leftTop + top01) >> 2);
        list[9] = (byte)((top01 + top12) >> 2);
        break;

      case 7:
      {
        int t4 = references[H264Workspace.Above(4)];
        int t5 = references[H264Workspace.Above(5)];
        int t6 = references[H264Workspace.Above(6)];
        var top34 = 1 + t3 + t4;
        var top45 = 1 + t4 + t5;
        var top56 = 1 + t5 + t6;

        list[0] = (byte)(top01 >> 1);
        list[1] = (byte)(top12 >> 1);
        list[2] = (byte)(top23 >> 1);
        list[3] = (byte)(top34 >> 1);
        list[4] = (byte)(top45 >> 1);
        list[5] = (byte)((top01 + top12) >> 2);
        list[6] = (byte)((top12 + top23) >> 2);
        list[7] = (byte)((top23 + top34) >> 2);
        list[8] = (byte)((top34 + top45) >> 2);
        list[9] = (byte)((top45 + top56) >> 2);
        break;
      }

      case 8:
        list[0] = (byte)(left01 >> 1);
        list[1] = (byte)((left01 + left12) >> 2);
        list[2] = (byte)(left12 >> 1);
        list[3] = (byte)((left12 + left23) >> 2);
        list[4] = (byte)(left23 >> 1);
        list[5] = (byte)((1 + left23 + (l3 << 1)) >> 2);
        list[6] = (byte)l3;
        list[7] = (byte)l3;
        list[8] = (byte)l3;
        list[9] = (byte)l3;
        break;
    }
  }

  /// <summary>
  /// The nine 8x8 modes, numbered as the 4x4 ones are. Two things separate them: the reference
  /// samples are smoothed before anything reads them, and a block covers four output samples
  /// rather than one.
  ///
  /// That second point is why this forms the block instead of windowing a list the way the 4x4
  /// modes do. A 4x4 average collapses to a single coefficient and the samples never need to
  /// exist; a quarter of an 8x8 keeps every basis function that does not change sign inside it, so
  /// the samples are wanted either way and the window buys nothing.
  /// </summary>
  public static void Predict8x8(in H264Workspace work, int mode, H264Neighbours found)
  {
    var observer = work.Observer;
    observer?.Begin(ReconstructionPhase.Smooth);

    var references = work.References;
    Span<byte> top = stackalloc byte[16];
    Span<byte> left = stackalloc byte[8];
    Span<byte> block = stackalloc byte[64];

    Smooth8x8(references, top, left, found);

    var corner = (byte)((references[H264Workspace.Left(8, 0)]
      + (references[H264Workspace.Corner] << 1) + references[H264Workspace.Above(0)] + 2) >> 2);

    observer?.End(ReconstructionPhase.Smooth);
    observer?.Begin(ReconstructionPhase.Predict);

    if (mode == 2)
      block.Fill(Dc8x8(top, left, found));
    else
      Directional8x8(block, top, left, corner, mode);

    Reduce8x8(work, block);
    observer?.End(ReconstructionPhase.Predict);
  }

  /// <summary>
  /// The mode is constant across the block, so the branch inside the walk costs a prediction the
  /// hardware gets right sixty-three times out of sixty-four. Handing each mode in as a delegate
  /// would read better and cost an indirect call per sample instead.
  /// </summary>
  private static void Directional8x8(
    Span<byte> block, ReadOnlySpan<byte> top, ReadOnlySpan<byte> left, byte corner, int mode)
  {
    for (var y = 0; y < 8; y++)
      for (var x = 0; x < 8; x++)
        block[y * 8 + x] = (byte)(mode switch
        {
          0 => VerticalAt(x, y, top, left, corner),
          1 => HorizontalAt(x, y, top, left, corner),
          3 => DownLeftAt(x, y, top, left, corner),
          4 => DownRightAt(x, y, top, left, corner),
          5 => VerticalRightAt(x, y, top, left, corner),
          6 => HorizontalDownAt(x, y, top, left, corner),
          7 => VerticalLeftAt(x, y, top, left, corner),
          _ => HorizontalUpAt(x, y, top, left, corner),
        });
  }

  /// <summary>
  /// Both ends of each run are smoothed against whatever lies past them, so where nothing does the
  /// end sample stands in for its own neighbour. A missing above-right run is not smoothed at all:
  /// the samples repeated into it carry no detail for smoothing to preserve, and the run before it
  /// then ends early against its own last sample.
  /// </summary>
  private static void Smooth8x8(
    ReadOnlySpan<byte> references, Span<byte> top, Span<byte> left, H264Neighbours found)
  {
    var corner = (int)references[H264Workspace.Corner];
    var hasTopLeft = (found & H264Neighbours.TopLeft) != 0;

    int aboveFirst = references[H264Workspace.Above(0)];
    top[0] = (byte)(hasTopLeft
      ? (corner + (aboveFirst << 1) + references[H264Workspace.Above(1)] + 2) >> 2
      : (aboveFirst * 3 + references[H264Workspace.Above(1)] + 2) >> 2);

    var run = (found & H264Neighbours.TopRight) != 0 ? 15 : 7;
    for (var i = 1; i < run; i++)
      top[i] = (byte)((references[H264Workspace.Above(i - 1)]
        + (references[H264Workspace.Above(i)] << 1)
        + references[H264Workspace.Above(i + 1)] + 2) >> 2);

    top[run] = (byte)((references[H264Workspace.Above(run - 1)]
      + references[H264Workspace.Above(run)] * 3 + 2) >> 2);

    for (var i = run + 1; i < 16; i++)
      top[i] = references[H264Workspace.Above(7)];

    int leftFirst = references[H264Workspace.Left(8, 0)];
    left[0] = (byte)(hasTopLeft
      ? (corner + (leftFirst << 1) + references[H264Workspace.Left(8, 1)] + 2) >> 2
      : (leftFirst * 3 + references[H264Workspace.Left(8, 1)] + 2) >> 2);

    for (var i = 1; i < 7; i++)
      left[i] = (byte)((references[H264Workspace.Left(8, i - 1)]
        + (references[H264Workspace.Left(8, i)] << 1)
        + references[H264Workspace.Left(8, i + 1)] + 2) >> 2);

    left[7] = (byte)((references[H264Workspace.Left(8, 6)]
      + references[H264Workspace.Left(8, 7)] * 3 + 2) >> 2);
  }

  private static byte Dc8x8(ReadOnlySpan<byte> top, ReadOnlySpan<byte> left, H264Neighbours found)
  {
    var hasLeft = (found & H264Neighbours.Left) != 0;
    var hasTop = (found & H264Neighbours.Top) != 0;

    var total = 0;
    for (var i = 0; i < 8; i++)
    {
      if (hasTop) total += top[i];
      if (hasLeft) total += left[i];
    }

    if (hasLeft && hasTop) return (byte)((total + 8) >> 4);
    if (hasLeft || hasTop) return (byte)((total + 4) >> 3);
    return Neutral;
  }

  private static int VerticalAt(
    int x, int y, ReadOnlySpan<byte> top, ReadOnlySpan<byte> left, byte corner) => top[x];

  private static int HorizontalAt(
    int x, int y, ReadOnlySpan<byte> top, ReadOnlySpan<byte> left, byte corner) => left[y];

  private static int DownLeftAt(
    int x, int y, ReadOnlySpan<byte> top, ReadOnlySpan<byte> left, byte corner) =>
    x == 7 && y == 7
      ? (top[14] + 3 * top[15] + 2) >> 2
      : (top[y + x] + (top[y + x + 1] << 1) + top[y + x + 2] + 2) >> 2;

  private static int DownRightAt(
    int x, int y, ReadOnlySpan<byte> top, ReadOnlySpan<byte> left, byte corner)
  {
    if (x > y + 1) return (top[x - y - 2] + (top[x - y - 1] << 1) + top[x - y] + 2) >> 2;
    if (x == y + 1) return (corner + (top[0] << 1) + top[1] + 2) >> 2;
    if (x == y) return (top[0] + (corner << 1) + left[0] + 2) >> 2;
    if (x == y - 1) return (corner + (left[0] << 1) + left[1] + 2) >> 2;
    return (left[y - x - 2] + (left[y - x - 1] << 1) + left[y - x] + 2) >> 2;
  }

  private static int VerticalRightAt(
    int x, int y, ReadOnlySpan<byte> top, ReadOnlySpan<byte> left, byte corner)
  {
    var diagonal = (x << 1) - y;
    var step = x - (y >> 1);

    if (diagonal >= 0)
    {
      if ((diagonal & 1) == 0)
        return step > 0 ? (top[step - 1] + top[step] + 1) >> 1 : (corner + top[0] + 1) >> 1;

      return step > 1
        ? (top[step - 2] + (top[step - 1] << 1) + top[step] + 2) >> 2
        : (corner + (top[0] << 1) + top[1] + 2) >> 2;
    }

    if (diagonal == -1) return (left[0] + (corner << 1) + top[0] + 2) >> 2;
    if (diagonal < -2)
      return (left[-diagonal - 1] + (left[-diagonal - 2] << 1) + left[-diagonal - 3] + 2) >> 2;

    return (left[1] + (left[0] << 1) + corner + 2) >> 2;
  }

  private static int HorizontalDownAt(
    int x, int y, ReadOnlySpan<byte> top, ReadOnlySpan<byte> left, byte corner)
  {
    var diagonal = (y << 1) - x;
    var step = y - (x >> 1);

    if (diagonal >= 0)
    {
      if ((diagonal & 1) == 0)
        return step == 0 ? (corner + left[0] + 1) >> 1 : (left[step - 1] + left[step] + 1) >> 1;

      return step == 1
        ? (corner + (left[0] << 1) + left[1] + 2) >> 2
        : (left[step - 2] + (left[step - 1] << 1) + left[step] + 2) >> 2;
    }

    if (diagonal == -1) return (left[0] + (corner << 1) + top[0] + 2) >> 2;
    if (diagonal < -2)
      return (top[-diagonal - 1] + (top[-diagonal - 2] << 1) + top[-diagonal - 3] + 2) >> 2;

    return (top[1] + (top[0] << 1) + corner + 2) >> 2;
  }

  private static int VerticalLeftAt(
    int x, int y, ReadOnlySpan<byte> top, ReadOnlySpan<byte> left, byte corner)
  {
    var half = y >> 1;
    return (y & 1) == 0
      ? (top[x + half] + top[x + half + 1] + 1) >> 1
      : (top[x + half] + (top[x + half + 1] << 1) + top[x + half + 2] + 2) >> 2;
  }

  private static int HorizontalUpAt(
    int x, int y, ReadOnlySpan<byte> top, ReadOnlySpan<byte> left, byte corner)
  {
    var diagonal = x + (y << 1);
    if (diagonal > 13) return left[7];
    if (diagonal == 13) return (left[6] + 3 * left[7] + 2) >> 2;

    var step = diagonal >> 1;
    return (diagonal & 1) == 0
      ? (left[step] + left[step + 1] + 1) >> 1
      : (left[step] + (left[step + 1] << 1) + left[step + 2] + 2) >> 2;
  }

  private static void Reduce8x8(in H264Workspace work, ReadOnlySpan<byte> block)
  {
    for (var i = 0; i < 8; i++)
    {
      work.Bottom[i] = block[7 * 8 + i];
      work.Right[i] = block[i * 8 + 7];
    }

    for (var cy = 0; cy < 2; cy++)
      for (var cx = 0; cx < 2; cx++)
      {
        var total = 0;
        for (var y = 0; y < 4; y++)
          for (var x = 0; x < 4; x++)
            total += block[(cy * 4 + y) * 8 + cx * 4 + x];

        work.Means[cy * 2 + cx] = (byte)((total + 8) >> 4);
      }
  }

  /// <summary>
  /// The four whole-macroblock luma modes. Prediction covers all sixteen rows at once and the 4x4
  /// residuals land underneath it, so unlike the 4x4 modes there is no chain inside the block.
  /// </summary>
  public static void Predict16x16(in H264Workspace work, int mode, H264Neighbours found)
  {
    var observer = work.Observer;
    observer?.Begin(ReconstructionPhase.Predict);

    switch (mode)
    {
      case 0: Vertical(work, 16, 4); break;
      case 1: Horizontal(work, 16, 4); break;
      case 2: Flat(work, 16, 4, Dc(work.References, 16, found)); break;
      default: Plane(work, 16, 4, 8, 5, 6, 7); break;
    }

    observer?.End(ReconstructionPhase.Predict);
  }

  /// <summary>
  /// The four chroma modes, numbered differently from luma: DC is nought here, not two. Both
  /// planes take the same mode, so the caller runs this once per plane over its own references.
  /// </summary>
  public static void PredictChroma(in H264Workspace work, int mode, H264Neighbours found)
  {
    var observer = work.Observer;
    observer?.Begin(ReconstructionPhase.Predict);

    switch (mode)
    {
      case 0: ChromaDc(work, found); break;
      case 1: Horizontal(work, 8, 2); break;
      case 2: Vertical(work, 8, 2); break;
      default: Plane(work, 8, 2, 4, 17, 5, 3); break;
    }

    observer?.End(ReconstructionPhase.Predict);
  }

  private static void Vertical(in H264Workspace work, int size, int cells)
  {
    var references = work.References;
    var group = size / cells;
    var last = references[H264Workspace.Above(size - 1)];

    for (var i = 0; i < size; i++)
    {
      work.Bottom[i] = references[H264Workspace.Above(i)];
      work.Right[i] = last;
    }

    for (var cx = 0; cx < cells; cx++)
    {
      var total = 0;
      for (var i = 0; i < group; i++)
        total += references[H264Workspace.Above(cx * group + i)];

      var mean = (byte)((total + (group >> 1)) / group);
      for (var cy = 0; cy < cells; cy++)
        work.Means[cy * cells + cx] = mean;
    }
  }

  private static void Horizontal(in H264Workspace work, int size, int cells)
  {
    var references = work.References;
    var group = size / cells;
    var last = references[H264Workspace.Left(size, size - 1)];

    for (var i = 0; i < size; i++)
    {
      work.Bottom[i] = last;
      work.Right[i] = references[H264Workspace.Left(size, i)];
    }

    for (var cy = 0; cy < cells; cy++)
    {
      var total = 0;
      for (var i = 0; i < group; i++)
        total += references[H264Workspace.Left(size, cy * group + i)];

      var mean = (byte)((total + (group >> 1)) / group);
      for (var cx = 0; cx < cells; cx++)
        work.Means[cy * cells + cx] = mean;
    }
  }

  private static byte Dc(ReadOnlySpan<byte> references, int size, H264Neighbours found)
  {
    var left = 0;
    var top = 0;
    for (var i = 0; i < size; i++)
    {
      left += references[H264Workspace.Left(size, i)];
      top += references[H264Workspace.Above(i)];
    }

    var hasLeft = (found & H264Neighbours.Left) != 0;
    var hasTop = (found & H264Neighbours.Top) != 0;

    if (hasLeft && hasTop) return (byte)((left + top + size) / (2 * size));
    if (hasLeft) return (byte)((left + (size >> 1)) / size);
    if (hasTop) return (byte)((top + (size >> 1)) / size);
    return Neutral;
  }

  /// <summary>
  /// Chroma DC is the one mode that varies across the block: each 4x4 quadrant takes its own mean,
  /// from whichever of its own edges exist. At this reduction a quadrant is exactly one output
  /// sample, so the four means are the four cells and nothing has to be averaged again.
  /// </summary>
  private static void ChromaDc(in H264Workspace work, H264Neighbours found)
  {
    var references = work.References;
    var hasLeft = (found & H264Neighbours.Left) != 0;
    var hasTop = (found & H264Neighbours.Top) != 0;

    Span<int> top = stackalloc int[2];
    Span<int> left = stackalloc int[2];
    for (var half = 0; half < 2; half++)
      for (var i = 0; i < 4; i++)
      {
        top[half] += references[H264Workspace.Above(half * 4 + i)];
        left[half] += references[H264Workspace.Left(8, half * 4 + i)];
      }

    for (var cy = 0; cy < 2; cy++)
      for (var cx = 0; cx < 2; cx++)
      {
        int mean;
        if (hasTop && hasLeft)
          // The two quadrants on the diagonal see both edges equally and average them. The other
          // two sit against one edge and take that one alone.
          mean = cx == cy ? (top[cx] + left[cy] + 4) >> 3
            : cx > cy ? (top[cx] + 2) >> 2
            : (left[cy] + 2) >> 2;
        else if (hasTop)
          mean = (top[cx] + 2) >> 2;
        else if (hasLeft)
          mean = (left[cy] + 2) >> 2;
        else
          mean = Neutral;

        work.Means[cy * 2 + cx] = (byte)mean;
      }

    for (var i = 0; i < 4; i++)
    {
      work.Bottom[i] = work.Means[2];
      work.Bottom[4 + i] = work.Means[3];
      work.Right[i] = work.Means[1];
      work.Right[4 + i] = work.Means[3];
    }
  }

  /// <summary>
  /// A linear ramp fitted to the two edges. Its average over a cell is its value at that cell's
  /// centre, so the cells are read off the same expression the edges are, at doubled coordinates to
  /// carry the half-sample the centre of an even-sized cell falls on.
  /// </summary>
  private static void Plane(
    in H264Workspace work, int size, int cells, int span, int weight, int shift, int centre)
  {
    var references = work.References;
    var last = size - 1;

    // The gradient pairs each sample with its mirror about the block's midpoint, and the last pair
    // reaches one back past the start of both edges - which is the corner. Reading that as index
    // -1 of either edge lands somewhere else entirely.
    var corner = references[H264Workspace.Corner];
    var horizontal = 0;
    var vertical = 0;

    for (var i = 0; i < span; i++)
    {
      var back = span - 2 - i;
      var behindAbove = back < 0 ? corner : references[H264Workspace.Above(back)];
      var behindLeft = back < 0 ? corner : references[H264Workspace.Left(size, back)];

      horizontal += (i + 1) * (references[H264Workspace.Above(span + i)] - behindAbove);
      vertical += (i + 1) * (references[H264Workspace.Left(size, span + i)] - behindLeft);
    }

    var origin = (references[H264Workspace.Left(size, last)] + references[H264Workspace.Above(last)])
      << 4;
    var alongRow = (weight * horizontal + (1 << (shift - 1))) >> shift;
    var downColumn = (weight * vertical + (1 << (shift - 1))) >> shift;

    for (var i = 0; i < size; i++)
    {
      work.Bottom[i] = Clip(origin + alongRow * (i - centre) + downColumn * (last - centre) + 16, 5);
      work.Right[i] = Clip(origin + alongRow * (last - centre) + downColumn * (i - centre) + 16, 5);
    }

    var group = size / cells;
    var offset = group - 1 - 2 * centre;

    for (var cy = 0; cy < cells; cy++)
      for (var cx = 0; cx < cells; cx++)
        work.Means[cy * cells + cx] = Clip(
          2 * origin
          + alongRow * (2 * cx * group + offset)
          + downColumn * (2 * cy * group + offset)
          + 32,
          6);
  }

  private static byte Clip(int value, int shift) =>
    (byte)Math.Clamp(value >> shift, 0, 255);

  /// <summary>
  /// Fills the reference array from the band for the neighbours <paramref name="found"/> says are
  /// there. Above-right is the one gap that gets filled rather than reported: when it is missing but
  /// the row above is not, it repeats that row's last sample, which is what the modes reading it
  /// expect to find.
  /// </summary>
  public static void Reference(
    in H264Neighbourhood view, in H264Workspace work, int x0, int y0, int size,
    H264Neighbours found)
  {
    var observer = work.Observer;
    observer?.Begin(ReconstructionPhase.Gather);

    Gather(view, work.References, x0, y0, size, found);

    observer?.End(ReconstructionPhase.Gather);
  }

  /// <summary>The same for the chroma pair, which shares a geometry and so asks its questions once.</summary>
  public static void ReferencePair(
    in H264Neighbourhood view, in H264Workspace work, int x0, int y0, int size,
    H264Neighbours found, byte[] secondBand, Span<byte> second)
  {
    var observer = work.Observer;
    observer?.Begin(ReconstructionPhase.Gather);

    GatherPair(view, work.References, secondBand, second, x0, y0, size, found);

    observer?.End(ReconstructionPhase.Gather);
  }

  private static void Gather(
    in H264Neighbourhood view, Span<byte> references, int x0, int y0, int size,
    H264Neighbours found)
  {
    var band = view.Band;
    var bandWidth = view.BandWidth;
    var above = y0 - 1;
    var column = x0 - 1;

    if ((found & H264Neighbours.TopLeft) != 0)
      references[H264Workspace.Corner] = band[(above - view.BandTop) * bandWidth + column];

    if ((found & H264Neighbours.Top) != 0)
    {
      var row = (above - view.BandTop) * bandWidth + x0;
      for (var i = 0; i < size; i++)
        references[H264Workspace.Above(i)] = band[row + i];

      if ((found & H264Neighbours.TopRight) != 0)
      {
        row += size;
        for (var i = 0; i < size; i++)
          references[H264Workspace.Above(size + i)] = band[row + i];
      }
      else
      {
        var repeated = references[H264Workspace.Above(size - 1)];
        for (var i = size; i < 2 * size; i++)
          references[H264Workspace.Above(i)] = repeated;
      }
    }

    if ((found & H264Neighbours.Left) != 0)
    {
      var at = (y0 - view.BandTop) * bandWidth + column;
      for (var i = 0; i < size; i++, at += bandWidth)
        references[H264Workspace.Left(size, i)] = band[at];
    }
  }

  /// <summary>
  /// The same walk for two planes that share a geometry, so the samples that come back differ but
  /// nothing about where they are does.
  ///
  /// Spelled out rather than folded into <see cref="Gather"/> with a flag. Luma never pairs and
  /// walks most of the blocks in the picture, so a test per run it can never take costs it more
  /// than the call this saves.
  /// </summary>
  private static void GatherPair(
    in H264Neighbourhood view, Span<byte> references, byte[] secondBand, Span<byte> second,
    int x0, int y0, int size, H264Neighbours found)
  {
    var band = view.Band;
    var bandWidth = view.BandWidth;
    var above = y0 - 1;
    var column = x0 - 1;

    if ((found & H264Neighbours.TopLeft) != 0)
    {
      var at = (above - view.BandTop) * bandWidth + column;
      references[H264Workspace.Corner] = band[at];
      second[H264Workspace.Corner] = secondBand[at];
    }

    if ((found & H264Neighbours.Top) != 0)
    {
      var row = (above - view.BandTop) * bandWidth + x0;
      for (var i = 0; i < size; i++)
      {
        references[H264Workspace.Above(i)] = band[row + i];
        second[H264Workspace.Above(i)] = secondBand[row + i];
      }

      if ((found & H264Neighbours.TopRight) != 0)
      {
        row += size;
        for (var i = 0; i < size; i++)
        {
          references[H264Workspace.Above(size + i)] = band[row + i];
          second[H264Workspace.Above(size + i)] = secondBand[row + i];
        }
      }
      else
      {
        var repeated = references[H264Workspace.Above(size - 1)];
        var repeatedSecond = second[H264Workspace.Above(size - 1)];
        for (var i = size; i < 2 * size; i++)
        {
          references[H264Workspace.Above(i)] = repeated;
          second[H264Workspace.Above(i)] = repeatedSecond;
        }
      }
    }

    if ((found & H264Neighbours.Left) != 0)
    {
      var at = (y0 - view.BandTop) * bandWidth + column;
      for (var i = 0; i < size; i++, at += bandWidth)
      {
        references[H264Workspace.Left(size, i)] = band[at];
        second[H264Workspace.Left(size, i)] = secondBand[at];
      }
    }
  }
}
