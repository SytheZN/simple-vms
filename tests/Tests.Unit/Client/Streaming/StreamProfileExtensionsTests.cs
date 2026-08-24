using Client.Core.Streaming;
using Shared.Api;

namespace Tests.Unit.Client.Streaming;

[TestFixture]
public class StreamProfileExtensionsTests
{
  private static StreamProfileDto Stream(
    string profile, StreamKind kind, string resolution, decimal fps = 30) =>
    new()
    {
      Profile = profile,
      Kind = kind,
      FormatId = "fmp4",
      Codec = "h264",
      Resolution = resolution,
      Fps = fps,
      RecordingEnabled = false
    };

  /// <summary>
  /// SCENARIO:
  /// A camera advertises several quality streams of differing resolution
  ///
  /// ACTION:
  /// Ask for the lowest and the highest preferred stream
  ///
  /// EXPECTED RESULT:
  /// Selection is by frame area, not by declaration order or profile name
  /// </summary>
  [Test]
  public void FirstPreferred_RanksByFrameArea()
  {
    StreamProfileDto[] streams =
    [
      Stream("mid", StreamKind.Quality, "1280x720"),
      Stream("high", StreamKind.Quality, "1920x1080"),
      Stream("low", StreamKind.Quality, "640x360")
    ];

    Assert.Multiple(() =>
    {
      Assert.That(streams.FirstPreferred(Quality.Lowest)!.Profile, Is.EqualTo("low"));
      Assert.That(streams.FirstPreferred(Quality.Highest)!.Profile, Is.EqualTo("high"));
    });
  }

  /// <summary>
  /// SCENARIO:
  /// Metadata streams sit alongside the quality streams
  ///
  /// ACTION:
  /// Ask for the lowest preferred stream where a metadata stream is the smallest
  ///
  /// EXPECTED RESULT:
  /// Only quality streams are considered, so the thumbnail is not selected
  /// </summary>
  [Test]
  public void FirstPreferred_IgnoresNonQualityStreams()
  {
    StreamProfileDto[] streams =
    [
      Stream("main-thumbnail", StreamKind.Metadata, "160x90"),
      Stream("main", StreamKind.Quality, "1920x1080")
    ];

    Assert.That(streams.FirstPreferred(Quality.Lowest)!.Profile, Is.EqualTo("main"));
  }

  /// <summary>
  /// SCENARIO:
  /// A stream reports a resolution that cannot be parsed
  ///
  /// ACTION:
  /// Ask for the lowest and the highest preferred stream
  ///
  /// EXPECTED RESULT:
  /// The unparseable stream ranks as neither extreme, so it is never mistaken for the cheap one
  /// </summary>
  [Test]
  public void FirstPreferred_LeavesUnparseableResolutionUnranked()
  {
    StreamProfileDto[] streams =
    [
      Stream("unknown", StreamKind.Quality, "who knows"),
      Stream("low", StreamKind.Quality, "640x360"),
      Stream("high", StreamKind.Quality, "1920x1080")
    ];

    Assert.Multiple(() =>
    {
      Assert.That(streams.FirstPreferred(Quality.Lowest)!.Profile, Is.EqualTo("low"));
      Assert.That(streams.FirstPreferred(Quality.Highest)!.Profile, Is.EqualTo("high"));
    });
  }

  /// <summary>
  /// SCENARIO:
  /// Every quality stream reports an unparseable resolution
  ///
  /// ACTION:
  /// Ask for the lowest preferred stream
  ///
  /// EXPECTED RESULT:
  /// The first quality stream is returned rather than nothing at all
  /// </summary>
  [Test]
  public void FirstPreferred_NothingRankable_FallsBackToFirstQualityStream()
  {
    StreamProfileDto[] streams =
    [
      Stream("a", StreamKind.Quality, ""),
      Stream("b", StreamKind.Quality, "x")
    ];

    Assert.That(streams.FirstPreferred(Quality.Lowest)!.Profile, Is.EqualTo("a"));
  }

  /// <summary>
  /// SCENARIO:
  /// A camera advertises no quality streams
  ///
  /// ACTION:
  /// Ask for the lowest preferred stream
  ///
  /// EXPECTED RESULT:
  /// Null, so the caller decides what an unstreamable camera means
  /// </summary>
  [Test]
  public void FirstPreferred_NoQualityStreams_ReturnsNull()
  {
    StreamProfileDto[] streams = [Stream("main-thumbnail", StreamKind.Metadata, "160x90")];

    Assert.That(streams.FirstPreferred(Quality.Lowest), Is.Null);
  }

  /// <summary>
  /// SCENARIO:
  /// A caller wants a stream by its exact profile name
  ///
  /// ACTION:
  /// Look up a present and an absent name
  ///
  /// EXPECTED RESULT:
  /// Exact ordinal match, null when absent
  /// </summary>
  [Test]
  public void FirstByName_MatchesExactly()
  {
    StreamProfileDto[] streams =
    [
      Stream("main", StreamKind.Quality, "1920x1080"),
      Stream("sub", StreamKind.Quality, "640x360")
    ];

    Assert.Multiple(() =>
    {
      Assert.That(streams.FirstByName("sub")!.Resolution, Is.EqualTo("640x360"));
      Assert.That(streams.FirstByName("Sub"), Is.Null);
      Assert.That(streams.FirstByName("missing"), Is.Null);
    });
  }

  /// <summary>
  /// SCENARIO:
  /// A plugin derives a stream by appending its type to the parent profile
  ///
  /// ACTION:
  /// Look up the derived stream by type alone
  ///
  /// EXPECTED RESULT:
  /// The suffix matches without the caller knowing the parent profile
  /// </summary>
  [Test]
  public void FirstByType_MatchesAppendedSuffix()
  {
    StreamProfileDto[] streams =
    [
      Stream("main", StreamKind.Quality, "1920x1080"),
      Stream("sub-thumbnail", StreamKind.Metadata, "160x90")
    ];

    Assert.Multiple(() =>
    {
      Assert.That(streams.FirstByType("thumbnail")!.Profile, Is.EqualTo("sub-thumbnail"));
      Assert.That(streams.FirstByType("motion"), Is.Null);
    });
  }

  /// <summary>
  /// SCENARIO:
  /// A profile is named exactly like a type, with no parent prefix
  ///
  /// ACTION:
  /// Look it up by type
  ///
  /// EXPECTED RESULT:
  /// No match, because a type is only ever a suffix appended to a parent profile
  /// </summary>
  [Test]
  public void FirstByType_DoesNotMatchBareProfileName()
  {
    StreamProfileDto[] streams = [Stream("thumbnail", StreamKind.Metadata, "160x90")];

    Assert.That(streams.FirstByType("thumbnail"), Is.Null);
  }
}
