using Client.Core.Decoding.Diagnostics;

namespace Tests.Unit.Client.Decoding.Diagnostics;

[TestFixture]
public class PlaybackDiagnosticsTests
{
  private static PlaybackStats SampleStats() => new(
    BackendName: "FakeBackend",
    RendererName: "FakeRenderer",
    State: "Streaming",
    Mode: "Live",
    Rate: 1.0,
    CatchupRate: 1.0,
    PositionUs: 1_000_000,
    BufferUs: 250_000,
    FetcherGops: 4,
    FetcherBytes: 4096,
    DecodedGops: 2,
    DecodedFrames: 60,
    Buffering: false);

  /// <summary>
  /// SCENARIO:
  /// Settings.ShowOverlay reflects through Diagnostics.Enabled
  ///
  /// ACTION:
  /// Toggle ShowOverlay, read Enabled
  ///
  /// EXPECTED RESULT:
  /// Enabled tracks ShowOverlay live (no caching)
  /// </summary>
  [Test]
  public void Enabled_ReflectsSettingsLive()
  {
    var settings = new DiagnosticsSettings();
    var diag = new PlaybackDiagnostics(SampleStats, settings);

    Assert.That(diag.Enabled, Is.False);

    settings.ShowOverlay = true;
    Assert.That(diag.Enabled, Is.True);

    settings.ShowOverlay = false;
    Assert.That(diag.Enabled, Is.False);
  }

  /// <summary>
  /// SCENARIO:
  /// Snapshot pulls fresh stats from the supplier each call
  ///
  /// ACTION:
  /// Wire a counter-incrementing pull, call Snapshot twice
  ///
  /// EXPECTED RESULT:
  /// Pull is invoked twice; the returned PositionUs reflects each call
  /// </summary>
  [Test]
  public void Snapshot_InvokesPullEachCall()
  {
    var counter = 0L;
    PlaybackStats Pull() => SampleStats() with { PositionUs = ++counter };
    var diag = new PlaybackDiagnostics(Pull, new DiagnosticsSettings());

    var first = diag.Snapshot();
    var second = diag.Snapshot();

    Assert.Multiple(() =>
    {
      Assert.That(first.PositionUs, Is.EqualTo(1));
      Assert.That(second.PositionUs, Is.EqualTo(2));
    });
  }

  /// <summary>
  /// SCENARIO:
  /// RecordFrame is the public hook for the renderer to feed the timing graph
  ///
  /// ACTION:
  /// Call RecordFrame three times back-to-back
  ///
  /// EXPECTED RESULT:
  /// FrameTiming reports two samples (first establishes baseline, no delta)
  /// </summary>
  [Test]
  public void RecordFrame_FeedsFrameTiming()
  {
    var diag = new PlaybackDiagnostics(SampleStats, new DiagnosticsSettings());

    diag.RecordFrame();
    diag.RecordFrame();
    diag.RecordFrame();

    Assert.That(diag.FrameTiming.SampleCount, Is.EqualTo(2));
  }

  /// <summary>
  /// SCENARIO:
  /// FrameTiming is the same instance across calls (a renderer can hold a ref)
  ///
  /// ACTION:
  /// Read FrameTiming twice
  ///
  /// EXPECTED RESULT:
  /// Same reference returned both times
  /// </summary>
  [Test]
  public void FrameTiming_SameInstance()
  {
    var diag = new PlaybackDiagnostics(SampleStats, new DiagnosticsSettings());

    var a = diag.FrameTiming;
    var b = diag.FrameTiming;

    Assert.That(a, Is.SameAs(b));
  }

  /// <summary>
  /// SCENARIO:
  /// PlaybackStats is a value-equal record struct (snapshot identity matters
  /// for diff-based UI updates)
  ///
  /// ACTION:
  /// Build two structs with identical fields, compare
  ///
  /// EXPECTED RESULT:
  /// They are equal; mutation of one field via with-expression breaks equality
  /// </summary>
  [Test]
  public void PlaybackStats_RecordEquality()
  {
    var a = SampleStats();
    var b = SampleStats();
    var c = a with { Rate = 2.0 };

    Assert.Multiple(() =>
    {
      Assert.That(a, Is.EqualTo(b));
      Assert.That(a, Is.Not.EqualTo(c));
    });
  }
}
