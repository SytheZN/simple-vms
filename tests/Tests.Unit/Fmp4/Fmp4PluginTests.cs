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

  // CreatePipelineAsync tests require real SPS/PPS test vectors
  // since InitAsync reads from the data stream to initialize

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
    Environment = null!,
    LoggerFactory = NullPluginLoggerFactory.Instance
  };
}
