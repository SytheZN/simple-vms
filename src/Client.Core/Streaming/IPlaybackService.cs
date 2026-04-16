namespace Client.Core.Streaming;

public interface IPlaybackService
{
  Task<IVideoFeed> StartAsync(
    Guid cameraId, string profile, ulong from, ulong? to, CancellationToken ct);
  Task<IVideoFeed> SeekAsync(IVideoFeed current, ulong timestamp, CancellationToken ct);
  Task StopAsync(IVideoFeed feed, CancellationToken ct);
}
