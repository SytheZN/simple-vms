using Client.Core.Decoding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests.Unit.Client.Decoding;

[TestFixture]
public class DecodePipelineFactoryTests
{
  private static IConfiguration Cfg(string? overrideValue) =>
    new ConfigurationBuilder()
      .AddInMemoryCollection([new KeyValuePair<string, string?>("OverrideDecoder", overrideValue)])
      .Build();

  /// <summary>
  /// SCENARIO:
  /// No configuration is supplied at all (the host doesn't pass one)
  ///
  /// ACTION:
  /// Construct factory without config, Create(Main)
  ///
  /// EXPECTED RESULT:
  /// Returns a pair (auto path picks an HW-to-SW backend); both members non-null
  /// </summary>
  [Test]
  public void Create_NoConfig_DefaultsToAutoMain()
  {
    var factory = new DecodePipelineFactory(NullLoggerFactory.Instance);

    var pipeline = factory.Create(DecodeRole.Main);

    Assert.That(pipeline, Is.Not.Null);
    Assert.Multiple(() =>
    {
      Assert.That(pipeline!.Value.Backend, Is.Not.Null);
      Assert.That(pipeline.Value.Renderer, Is.Not.Null);
    });
    pipeline!.Value.Backend.Dispose();
    pipeline.Value.Renderer.Dispose();
  }

  /// <summary>
  /// SCENARIO:
  /// OverrideDecoder is empty string (treated as auto)
  ///
  /// ACTION:
  /// Construct factory with empty override, Create(Main)
  ///
  /// EXPECTED RESULT:
  /// Returns a pair (empty/null collapses to auto)
  /// </summary>
  [Test]
  public void Create_EmptyOverride_TreatedAsAuto()
  {
    var factory = new DecodePipelineFactory(NullLoggerFactory.Instance, Cfg(""));

    var pipeline = factory.Create(DecodeRole.Main);

    Assert.That(pipeline, Is.Not.Null);
    pipeline!.Value.Backend.Dispose();
    pipeline.Value.Renderer.Dispose();
  }

  /// <summary>
  /// SCENARIO:
  /// Tile role under auto policy must always pick the SW backend (HW decoder
  /// slot limits make HW unacceptable for 32 concurrent tiles)
  ///
  /// ACTION:
  /// Override = auto, Create(Tile)
  ///
  /// EXPECTED RESULT:
  /// Returns a pair; backend display name reflects software decode
  /// </summary>
  [Test]
  public void Create_AutoTile_PicksSwBackend()
  {
    var factory = new DecodePipelineFactory(NullLoggerFactory.Instance, Cfg("auto"));

    var pipeline = factory.Create(DecodeRole.Tile);

    Assert.That(pipeline, Is.Not.Null);
    Assert.That(pipeline!.Value.Backend.DisplayName,
      Does.Contain("Software").IgnoreCase.Or.Contain("SW").IgnoreCase);
    pipeline.Value.Backend.Dispose();
    pipeline.Value.Renderer.Dispose();
  }

  /// <summary>
  /// SCENARIO:
  /// Explicit "sw" override forces software decode regardless of role
  ///
  /// ACTION:
  /// Override = sw, Create(Main)
  ///
  /// EXPECTED RESULT:
  /// Returns a pair with the software backend
  /// </summary>
  [Test]
  public void Create_SwOverride_ForcesSwBackend()
  {
    var factory = new DecodePipelineFactory(NullLoggerFactory.Instance, Cfg("sw"));

    var pipeline = factory.Create(DecodeRole.Main);

    Assert.That(pipeline, Is.Not.Null);
    Assert.That(pipeline!.Value.Backend.DisplayName,
      Does.Contain("Software").IgnoreCase.Or.Contain("SW").IgnoreCase);
    pipeline.Value.Backend.Dispose();
    pipeline.Value.Renderer.Dispose();
  }

  /// <summary>
  /// SCENARIO:
  /// Override is a value the factory does not recognise
  ///
  /// ACTION:
  /// Override = "garbage", Create(Main)
  ///
  /// EXPECTED RESULT:
  /// Returns null (the unknown-value path logs an error and bails - the
  /// caller then refuses to start playback rather than silently downgrading)
  /// </summary>
  [Test]
  public void Create_UnknownOverride_ReturnsNull()
  {
    var factory = new DecodePipelineFactory(NullLoggerFactory.Instance, Cfg("garbage"));

    var pipeline = factory.Create(DecodeRole.Main);

    Assert.That(pipeline, Is.Null);
  }

  /// <summary>
  /// SCENARIO:
  /// Case-insensitive matching: override values are lowercased before matching
  ///
  /// ACTION:
  /// Override = "SW" (uppercase), Create(Main)
  ///
  /// EXPECTED RESULT:
  /// Resolves to software backend (case insensitivity is honoured)
  /// </summary>
  [Test]
  public void Create_OverrideIsCaseInsensitive()
  {
    var factory = new DecodePipelineFactory(NullLoggerFactory.Instance, Cfg("SW"));

    var pipeline = factory.Create(DecodeRole.Main);

    Assert.That(pipeline, Is.Not.Null);
    pipeline!.Value.Backend.Dispose();
    pipeline.Value.Renderer.Dispose();
  }
}
