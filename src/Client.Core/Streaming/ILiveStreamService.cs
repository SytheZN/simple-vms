namespace Client.Core.Streaming;

public interface ILiveStreamService
{
  event Action<IVideoFeed, IVideoFeed>? FeedReplaced;
  Task<IVideoFeed> SubscribeAsync(Guid cameraId, string profile, CancellationToken ct);
  Task UnsubscribeAsync(IVideoFeed feed, CancellationToken ct);
}
