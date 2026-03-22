using Server.Plugins;
using Shared.Models;
using Shared.Models.Dto;

namespace Server.Core.Services;

public sealed class RecordingService
{
  private readonly IPluginHost _plugins;

  public RecordingService(IPluginHost plugins)
  {
    _plugins = plugins;
  }

  public async Task<OneOf<IReadOnlyList<RecordingSegmentDto>, Error>> GetSegmentsAsync(
    Guid cameraId, string profile, ulong from, ulong to, CancellationToken ct)
  {
    var streamResult = await FindStreamAsync(cameraId, profile, ct);
    if (streamResult.IsT1) return streamResult.AsT1;

    var stream = streamResult.AsT0;
    var segments = await _plugins.DataProvider.Segments.GetByTimeRangeAsync(stream.Id, from, to, ct);
    return segments.Match<OneOf<IReadOnlyList<RecordingSegmentDto>, Error>>(
      segs => segs.Select(s => new RecordingSegmentDto
      {
        Id = s.Id,
        StartTime = s.StartTime,
        EndTime = s.EndTime,
        Profile = profile,
        SizeBytes = s.SizeBytes
      }).ToList(),
      error => error);
  }

  public async Task<OneOf<TimelineResponse, Error>> GetTimelineAsync(
    Guid cameraId, string profile, ulong from, ulong to, CancellationToken ct)
  {
    var streamResult = await FindStreamAsync(cameraId, profile, ct);
    if (streamResult.IsT1) return streamResult.AsT1;

    var stream = streamResult.AsT0;
    var segmentsResult = await _plugins.DataProvider.Segments.GetByTimeRangeAsync(stream.Id, from, to, ct);
    if (segmentsResult.IsT1)
      return segmentsResult.AsT1;

    var segments = segmentsResult.AsT0;
    var spans = MergeSpans(segments);

    var eventsResult = await _plugins.DataProvider.Events.GetByTimeRangeAsync(cameraId, from, to, ct);
    var events = eventsResult.Match(
      evts => evts.Select(e => new TimelineEvent
      {
        Id = e.Id,
        Type = e.Type,
        StartTime = e.StartTime,
        EndTime = e.EndTime
      }).ToList(),
      _ => new List<TimelineEvent>());

    return new TimelineResponse { Spans = spans, Events = events };
  }

  private async Task<OneOf<CameraStream, Error>> FindStreamAsync(
    Guid cameraId, string profile, CancellationToken ct)
  {
    var streams = await _plugins.DataProvider.Streams.GetByCameraIdAsync(cameraId, ct);
    if (streams.IsT1) return streams.AsT1;

    var match = streams.AsT0.FirstOrDefault(s => s.Profile == profile);
    if (match == null)
      return new Error(
        Result.NotFound,
        new DebugTag(ModuleIds.Recording, 0x0001),
        $"No stream with profile '{profile}' for camera {cameraId}");

    return match;
  }

  private static List<TimelineSpan> MergeSpans(IReadOnlyList<Segment> segments)
  {
    if (segments.Count == 0)
      return [];

    var spans = new List<TimelineSpan>();
    var start = segments[0].StartTime;
    var end = segments[0].EndTime;

    for (var i = 1; i < segments.Count; i++)
    {
      var seg = segments[i];
      if (seg.StartTime <= end + 1)
      {
        if (seg.EndTime > end) end = seg.EndTime;
      }
      else
      {
        spans.Add(new TimelineSpan { StartTime = start, EndTime = end });
        start = seg.StartTime;
        end = seg.EndTime;
      }
    }

    spans.Add(new TimelineSpan { StartTime = start, EndTime = end });
    return spans;
  }
}
