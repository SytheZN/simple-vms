using Shared.Api;
using Shared.Models;

namespace Client.Core.Streaming;

public static class StreamProfileExtensions
{
  public static StreamProfileDto? FirstPreferred(
    this IEnumerable<StreamProfileDto>? streams, Quality quality)
  {
    List<StreamProfileDto> candidates = [.. streams?.Where(s => s.Kind == StreamKind.Quality) ?? []];
    var ranked = candidates.Where(s => PixelCount(s) != null).ToList();
    if (ranked.Count == 0) return candidates.FirstOrDefault();

    return quality == Quality.Lowest
      ? ranked.MinBy(PixelCount)
      : ranked.MaxBy(PixelCount);
  }

  public static StreamProfileDto? FirstByName(
    this IEnumerable<StreamProfileDto>? streams, string name) =>
    streams?.FirstOrDefault(s => string.Equals(s.Profile, name, StringComparison.Ordinal));

  public static StreamProfileDto? FirstByType(
    this IEnumerable<StreamProfileDto>? streams, string type) =>
    streams?.FirstOrDefault(s => s.Profile.EndsWith($"-{type}", StringComparison.Ordinal));

  private static long? PixelCount(StreamProfileDto stream)
  {
    var separator = stream.Resolution.IndexOf('x', StringComparison.OrdinalIgnoreCase);
    if (separator <= 0) return null;

    return long.TryParse(stream.Resolution[..separator], out var width)
      && long.TryParse(stream.Resolution[(separator + 1)..], out var height)
        ? width * height
        : null;
  }
}
