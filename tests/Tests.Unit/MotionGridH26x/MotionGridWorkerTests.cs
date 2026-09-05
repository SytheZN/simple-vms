using Analyzer.MotionGridH26x;
using Microsoft.Extensions.Logging.Abstractions;
using Server.Streaming;
using Shared.Models.Formats;

namespace Tests.Unit.MotionGridH26x;

[TestFixture]
public class MotionGridWorkerTests
{
  private static MotionGridProcessor Processor() =>
    new(new ProcessorSettings(DetectionAlgorithm.Raw, 10, false, false), () => null, NullLogger.Instance);

  /// <summary>
  /// SCENARIO:
  /// An H.264 worker is constructed over a demanding tap on the parent fan-out
  ///
  /// ACTION:
  /// Dispose the worker
  ///
  /// EXPECTED RESULT:
  /// The tap is released and the parent fan-out has no subscribers left
  /// </summary>
  [Test]
  public async Task H264Worker_Dispose_ReleasesParentTap()
  {
    await using var parent = new DataStreamFanOut<H264NalUnit>(new StreamInfo { DataFormat = "h264" });
    var tap = parent.Subscribe();
    var worker = new MotionGridH264Worker(Guid.NewGuid(), "main", tap, Processor(), NullLogger.Instance);
    Assert.That(parent.SubscriberCount, Is.EqualTo(1));

    await worker.DisposeAsync();

    Assert.That(parent.SubscriberCount, Is.EqualTo(0));
  }

  /// <summary>
  /// SCENARIO:
  /// An H.265 worker is constructed over a demanding tap on the parent fan-out
  ///
  /// ACTION:
  /// Dispose the worker
  ///
  /// EXPECTED RESULT:
  /// The tap is released and the parent fan-out has no subscribers left
  /// </summary>
  [Test]
  public async Task H265Worker_Dispose_ReleasesParentTap()
  {
    await using var parent = new DataStreamFanOut<H265NalUnit>(new StreamInfo { DataFormat = "h265" });
    var tap = parent.Subscribe();
    var worker = new MotionGridH265Worker(Guid.NewGuid(), "main", tap, Processor(), NullLogger.Instance);
    Assert.That(parent.SubscriberCount, Is.EqualTo(1));

    await worker.DisposeAsync();

    Assert.That(parent.SubscriberCount, Is.EqualTo(0));
  }
}
