using Analyzer.MotionGridH26x;
using Shared.Models;

namespace Tests.Unit.MotionGridH26x;

[TestFixture]
public class MotionGridH26xSmokeTests
{
  /// <summary>
  /// SCENARIO:
  /// Plugin metadata is queried before Initialize is called
  ///
  /// ACTION:
  /// Construct the plugin and read Metadata, AnalyzerId, SupportedCodecs
  ///
  /// EXPECTED RESULT:
  /// Values match the constants documented for the plugin
  /// </summary>
  [Test]
  public void Plugin_StaticIdentity_MatchesContract()
  {
    var plugin = new MotionGridH26xPlugin();
    Assert.That(plugin.Metadata.Id, Is.EqualTo("motion-grid-h26x"));
    Assert.That(plugin.AnalyzerId, Is.EqualTo("motion-grid-h26x"));
    Assert.That(plugin.SupportedCodecs, Is.EqualTo(new[] { "h264", "h265" }));
  }

  /// <summary>
  /// SCENARIO:
  /// IPluginStreamSettings schema is queried for an h264 stream the registry knows about
  ///
  /// ACTION:
  /// Call GetSchema with the matching streamId
  ///
  /// EXPECTED RESULT:
  /// Returns one group with a single streamEnabled boolean field
  /// </summary>
  [Test]
  public void GetSchema_AppliesToH264Stream_ReturnsStreamEnabledField()
  {
    var streamId = Guid.NewGuid();
    var plugin = MotionGridTestHelpers.InitializedPlugin(streamId, codec: "h264");

    var schema = plugin.GetSchema(streamId);

    Assert.That(schema, Has.Count.EqualTo(1));
    Assert.That(schema[0].Fields, Has.Count.EqualTo(1));
    Assert.That(schema[0].Fields[0].Key, Is.EqualTo("streamEnabled"));
    Assert.That(schema[0].Fields[0].Type, Is.EqualTo("boolean"));
  }

  /// <summary>
  /// SCENARIO:
  /// IPluginStreamSettings schema is queried for an h265 stream the registry knows about
  ///
  /// ACTION:
  /// Call GetSchema with the matching streamId
  ///
  /// EXPECTED RESULT:
  /// Returns one group with a single streamEnabled boolean field
  /// </summary>
  [Test]
  public void GetSchema_AppliesToH265Stream_ReturnsStreamEnabledField()
  {
    var streamId = Guid.NewGuid();
    var plugin = MotionGridTestHelpers.InitializedPlugin(streamId, codec: "h265");

    var schema = plugin.GetSchema(streamId);

    Assert.That(schema, Has.Count.EqualTo(1));
    Assert.That(schema[0].Fields, Has.Count.EqualTo(1));
    Assert.That(schema[0].Fields[0].Key, Is.EqualTo("streamEnabled"));
    Assert.That(schema[0].Fields[0].Type, Is.EqualTo("boolean"));
  }

  /// <summary>
  /// SCENARIO:
  /// IPluginStreamSettings schema is queried for a stream with an unsupported codec
  ///
  /// ACTION:
  /// Call GetSchema with a stream the registry reports as mjpeg
  ///
  /// EXPECTED RESULT:
  /// Returns an empty list (plugin does not apply)
  /// </summary>
  [Test]
  public void GetSchema_DoesNotApplyToUnsupportedCodecStream_ReturnsEmpty()
  {
    var streamId = Guid.NewGuid();
    var plugin = MotionGridTestHelpers.InitializedPlugin(streamId, codec: "mjpeg");

    var schema = plugin.GetSchema(streamId);

    Assert.That(schema, Is.Empty);
  }

  /// <summary>
  /// SCENARIO:
  /// ValidateValue is called with a non-boolean string for streamEnabled
  ///
  /// ACTION:
  /// Call ValidateValue("streamEnabled", "maybe")
  ///
  /// EXPECTED RESULT:
  /// Returns Error with Result.BadRequest
  /// </summary>
  [Test]
  public void ValidateValue_RejectsNonBoolean()
  {
    var plugin = MotionGridTestHelpers.InitializedPlugin(Guid.NewGuid(), "h264");
    var result = plugin.ValidateValue(Guid.NewGuid(), "streamEnabled", "maybe");
    Assert.That(result.IsT1, Is.True);
    Assert.That(result.AsT1.Result, Is.EqualTo(Result.BadRequest));
  }
}
