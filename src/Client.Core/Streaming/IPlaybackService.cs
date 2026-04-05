namespace Client.Core.Streaming;

public interface IPlaybackService
{
  Task<VideoFeed> StartAsync(
    Guid cameraId, string profile, ulong from, ulong? to, CancellationToken ct);
  Task<VideoFeed> SeekAsync(VideoFeed current, ulong timestamp, CancellationToken ct);
  Task StopAsync(VideoFeed feed, CancellationToken ct);
}
