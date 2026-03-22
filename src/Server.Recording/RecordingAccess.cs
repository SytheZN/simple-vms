using Server.Plugins;
using Shared.Models;

namespace Server.Recording;

public sealed class RecordingAccess : IRecordingAccess
{
  private readonly IPluginHost _plugins;

  public RecordingAccess(IPluginHost plugins)
  {
    _plugins = plugins;
  }

  public async Task<OneOf<IReadOnlyList<SegmentInfo>, Error>> QueryAsync(
    Guid cameraId, string profile, ulong from, ulong to, CancellationToken ct)
  {
    var streamResult = await FindStreamAsync(cameraId, profile, ct);
    if (streamResult.IsT1)
      return streamResult.AsT1;

    var segmentsResult = await _plugins.DataProvider.Segments
      .GetByTimeRangeAsync(streamResult.AsT0.Id, from, to, ct);

    return segmentsResult.Match<OneOf<IReadOnlyList<SegmentInfo>, Error>>(
      segments => segments.Select(s => new SegmentInfo
      {
        Id = s.Id,
        StartTime = s.StartTime,
        EndTime = s.EndTime,
        SegmentRef = s.SegmentRef,
        SizeBytes = s.SizeBytes
      }).ToList(),
      error => error);
  }

  public async Task<OneOf<Stream, Error>> OpenSegmentAsync(string segmentRef, CancellationToken ct)
  {
    var storage = _plugins.StorageProviders.FirstOrDefault();
    if (storage == null)
      return Error.Create(ModuleIds.Recording, 0x0002, Result.Unavailable,
        "No storage provider available");

    try
    {
      return await storage.OpenReadAsync(segmentRef, ct);
    }
    catch (FileNotFoundException)
    {
      return Error.Create(ModuleIds.Recording, 0x0003, Result.NotFound,
        $"Segment not found: {segmentRef}");
    }
  }

  private async Task<OneOf<CameraStream, Error>> FindStreamAsync(
    Guid cameraId, string profile, CancellationToken ct)
  {
    var streamsResult = await _plugins.DataProvider.Streams.GetByCameraIdAsync(cameraId, ct);
    if (streamsResult.IsT1)
      return streamsResult.AsT1;

    var match = streamsResult.AsT0.FirstOrDefault(s => s.Profile == profile);
    if (match == null)
      return Error.Create(ModuleIds.Recording, 0x0001, Result.NotFound,
        $"No stream with profile '{profile}' for camera {cameraId}");

    return match;
  }
}
