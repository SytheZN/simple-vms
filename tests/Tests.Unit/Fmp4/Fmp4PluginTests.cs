using Format.Fmp4;
using Shared.Models;
using Shared.Models.Formats;

namespace Tests.Unit.Fmp4;

[TestFixture]
public class Fmp4PluginTests
{
  /// <summary>
  /// SCENARIO:
  /// H.264 plugin has correct metadata and type registration
  ///
  /// ACTION:
  /// Inspect plugin properties
  ///
  /// EXPECTED RESULT:
  /// InputType is H264NalUnit, OutputType is Fmp4Fragment, FormatId is "fmp4"
  /// </summary>
  [Test]
  public void H264Plugin_HasCorrectTypeRegistration()
  {
    var plugin = new Fmp4H264Plugin();

    Assert.That(plugin.Metadata.Id, Is.EqualTo("fmp4-h264"));
    Assert.That(plugin.FormatId, Is.EqualTo("fmp4"));
    Assert.That(plugin.FileExtension, Is.EqualTo(".mp4"));
    Assert.That(plugin.InputType, Is.EqualTo(typeof(H264NalUnit)));
    Assert.That(plugin.OutputType, Is.EqualTo(typeof(Fmp4Fragment)));
  }

  /// <summary>
  /// SCENARIO:
  /// H.265 plugin has correct metadata and type registration
  ///
  /// ACTION:
  /// Inspect plugin properties
  ///
  /// EXPECTED RESULT:
  /// InputType is H265NalUnit, OutputType is Fmp4Fragment, FormatId is "fmp4"
  /// </summary>
  [Test]
  public void H265Plugin_HasCorrectTypeRegistration()
  {
    var plugin = new Fmp4H265Plugin();

    Assert.That(plugin.Metadata.Id, Is.EqualTo("fmp4-h265"));
    Assert.That(plugin.FormatId, Is.EqualTo("fmp4"));
    Assert.That(plugin.FileExtension, Is.EqualTo(".mp4"));
    Assert.That(plugin.InputType, Is.EqualTo(typeof(H265NalUnit)));
    Assert.That(plugin.OutputType, Is.EqualTo(typeof(Fmp4Fragment)));
  }

  /// <summary>
  /// SCENARIO:
  /// Plugin lifecycle methods succeed
  ///
  /// ACTION:
  /// Call Initialize, StartAsync, StopAsync
  ///
  /// EXPECTED RESULT:
  /// All return Success
  /// </summary>
  [Test]
  public async Task H264Plugin_LifecycleSucceeds()
  {
    var plugin = new Fmp4H264Plugin();

    var initResult = plugin.Initialize(CreateContext());
    Assert.That(initResult.IsT0, Is.True);

    var startResult = await plugin.StartAsync(CancellationToken.None);
    Assert.That(startResult.IsT0, Is.True);

    var stopResult = await plugin.StopAsync(CancellationToken.None);
    Assert.That(stopResult.IsT0, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// Plugin lifecycle methods succeed for H.265
  ///
  /// ACTION:
  /// Call Initialize, StartAsync, StopAsync
  ///
  /// EXPECTED RESULT:
  /// All return Success
  /// </summary>
  [Test]
  public async Task H265Plugin_LifecycleSucceeds()
  {
    var plugin = new Fmp4H265Plugin();

    var initResult = plugin.Initialize(CreateContext());
    Assert.That(initResult.IsT0, Is.True);

    var startResult = await plugin.StartAsync(CancellationToken.None);
    Assert.That(startResult.IsT0, Is.True);

    var stopResult = await plugin.StopAsync(CancellationToken.None);
    Assert.That(stopResult.IsT0, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// H.264 plugin creates a pipeline from H264NalUnit data stream
  ///
  /// ACTION:
  /// Call CreatePipeline with an H264NalUnit data stream
  ///
  /// EXPECTED RESULT:
  /// Returns an IVideoStream with FrameType Fmp4Fragment and format "fmp4"
  /// </summary>
  [Test]
  public void H264Plugin_CreatePipeline_ReturnsVideoStream()
  {
    var plugin = new Fmp4H264Plugin();
    var input = new TestDataStream<H264NalUnit>([]);
    var info = new StreamInfo { DataFormat = "h264", Resolution = "1920x1080", Fps = 30 };

    var result = plugin.CreatePipeline(input, info);

    Assert.That(result.IsT0, Is.True);
    var videoStream = result.AsT0;
    Assert.That(videoStream.FrameType, Is.EqualTo(typeof(Fmp4Fragment)));
    Assert.That(videoStream.Info.DataFormat, Is.EqualTo("fmp4"));
    Assert.That(videoStream.Info.Resolution, Is.EqualTo("1920x1080"));
  }

  /// <summary>
  /// SCENARIO:
  /// H.265 plugin creates a pipeline from H265NalUnit data stream
  ///
  /// ACTION:
  /// Call CreatePipeline with an H265NalUnit data stream
  ///
  /// EXPECTED RESULT:
  /// Returns an IVideoStream with FrameType Fmp4Fragment
  /// </summary>
  [Test]
  public void H265Plugin_CreatePipeline_ReturnsVideoStream()
  {
    var plugin = new Fmp4H265Plugin();
    var input = new TestDataStream<H265NalUnit>([]);
    var info = new StreamInfo { DataFormat = "h265", Resolution = "3840x2160", Fps = 25 };

    var result = plugin.CreatePipeline(input, info);

    Assert.That(result.IsT0, Is.True);
    var videoStream = result.AsT0;
    Assert.That(videoStream.FrameType, Is.EqualTo(typeof(Fmp4Fragment)));
    Assert.That(videoStream.Info.DataFormat, Is.EqualTo("fmp4"));
    Assert.That(videoStream.Info.Fps, Is.EqualTo(25));
  }

  /// <summary>
  /// SCENARIO:
  /// Plugin creates a segment reader from a stream
  ///
  /// ACTION:
  /// Call CreateReader with a MemoryStream
  ///
  /// EXPECTED RESULT:
  /// Returns an ISegmentReader
  /// </summary>
  [Test]
  public void H264Plugin_CreateReader_ReturnsSegmentReader()
  {
    var plugin = new Fmp4H264Plugin();
    var result = plugin.CreateReader(new MemoryStream());

    Assert.That(result.IsT0, Is.True);
  }

  private static PluginContext CreateContext() => new()
  {
    Config = null!,
    Environment = null!
  };
}
