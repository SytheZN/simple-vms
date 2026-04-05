namespace Client.Core.Streaming;

public interface ILiveStreamService
{
  event Action<VideoFeed, VideoFeed>? FeedReplaced;
  Task<VideoFeed> SubscribeAsync(Guid cameraId, string profile, CancellationToken ct);
  Task UnsubscribeAsync(VideoFeed feed, CancellationToken ct);
}
