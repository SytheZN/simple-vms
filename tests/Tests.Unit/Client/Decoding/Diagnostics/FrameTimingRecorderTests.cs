using Client.Core.Decoding.Diagnostics;

namespace Tests.Unit.Client.Decoding.Diagnostics;

[TestFixture]
public class FrameTimingRecorderTests
{
  /// <summary>
  /// SCENARIO:
  /// A fresh recorder has never been ticked
  ///
  /// ACTION:
  /// Inspect SampleCount and copy into a destination span
  ///
  /// EXPECTED RESULT:
  /// SampleCount is zero; CopyMs leaves the destination zero-filled
  /// </summary>
  [Test]
  public void Empty_NoSamples()
  {
    var rec = new FrameTimingRecorder();
    var dest = new double[8];
    Array.Fill(dest, -1);

    rec.CopyMs(dest);

    Assert.Multiple(() =>
    {
      Assert.That(rec.SampleCount, Is.Zero);
      Assert.That(dest, Is.All.Zero);
    });
  }

  /// <summary>
  /// SCENARIO:
  /// The first Record establishes a baseline; only the second produces a delta
  ///
  /// ACTION:
  /// Call Record once
  ///
  /// EXPECTED RESULT:
  /// SampleCount stays zero (no delta yet)
  /// </summary>
  [Test]
  public void FirstRecord_DoesNotProduceDelta()
  {
    var rec = new FrameTimingRecorder();

    rec.Record();

    Assert.That(rec.SampleCount, Is.Zero);
  }

  /// <summary>
  /// SCENARIO:
  /// Three Records produce two inter-frame deltas
  ///
  /// ACTION:
  /// Call Record 3 times back-to-back
  ///
  /// EXPECTED RESULT:
  /// SampleCount is 2
  /// </summary>
  [Test]
  public void ThreeRecords_ProduceTwoSamples()
  {
    var rec = new FrameTimingRecorder();

    rec.Record();
    rec.Record();
    rec.Record();

    Assert.That(rec.SampleCount, Is.EqualTo(2));
  }

  /// <summary>
  /// SCENARIO:
  /// More records arrive than the ring buffer holds
  ///
  /// ACTION:
  /// Call Record 200 times (Capacity is 120)
  ///
  /// EXPECTED RESULT:
  /// SampleCount caps at Capacity
  /// </summary>
  [Test]
  public void RecordPastCapacity_CountSaturatesAtCapacity()
  {
    var rec = new FrameTimingRecorder();

    for (var i = 0; i < 200; i++) rec.Record();

    Assert.That(rec.SampleCount, Is.EqualTo(FrameTimingRecorder.Capacity));
  }

  /// <summary>
  /// SCENARIO:
  /// CopyMs writes deltas to a destination larger than the recorded samples
  ///
  /// ACTION:
  /// Record 4 deltas, copy into a 10-slot destination prefilled with sentinels
  ///
  /// EXPECTED RESULT:
  /// Leading 7 slots are zero, trailing 3 slots are non-negative milliseconds
  /// (the recorder produces 3 deltas from 4 records; spec requires zero-padding)
  /// </summary>
  [Test]
  public void CopyMs_PadsLeadingZerosWhenSamplesUnderfill()
  {
    var rec = new FrameTimingRecorder();
    for (var i = 0; i < 4; i++)
    {
      rec.Record();
      Thread.Sleep(1);
    }

    var dest = new double[10];
    Array.Fill(dest, -1);

    rec.CopyMs(dest);

    Assert.Multiple(() =>
    {
      for (var i = 0; i < 7; i++) Assert.That(dest[i], Is.EqualTo(0), $"slot {i}");
      for (var i = 7; i < 10; i++) Assert.That(dest[i], Is.GreaterThan(0), $"slot {i}");
    });
  }

  /// <summary>
  /// SCENARIO:
  /// Destination span is shorter than the available samples
  ///
  /// ACTION:
  /// Record 50 deltas, CopyMs into a 5-slot destination
  ///
  /// EXPECTED RESULT:
  /// All 5 slots are populated with positive millisecond deltas
  /// </summary>
  [Test]
  public void CopyMs_DestinationShorterThanSamples_FillsAll()
  {
    var rec = new FrameTimingRecorder();
    for (var i = 0; i < 50; i++)
    {
      rec.Record();
      Thread.Sleep(1);
    }

    var dest = new double[5];
    rec.CopyMs(dest);

    Assert.That(dest, Is.All.GreaterThan(0));
  }
}
