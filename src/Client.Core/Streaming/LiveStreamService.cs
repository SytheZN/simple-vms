using Client.Core.Tunnel;
using MessagePack;
using Microsoft.Extensions.Logging;
using Shared.Protocol;

namespace Client.Core.Streaming;

public sealed class LiveStreamService : ILiveStreamService, IDisposable
{
  private readonly ITunnelService _tunnel;
  private readonly ILogger<LiveStreamService> _logger;
  private readonly Lock _lock = new();
  private readonly List<ActiveSubscription> _subscriptions = [];
  private CancellationTokenSource? _reconnectCts;

  public event Action<VideoFeed, VideoFeed>? FeedReplaced;

  public LiveStreamService(ITunnelService tunnel, ILogger<LiveStreamService> logger)
  {
    _tunnel = tunnel;
    _logger = logger;
    _tunnel.StateChanged += OnStateChanged;
  }

  public async Task<VideoFeed> SubscribeAsync(Guid cameraId, string profile, CancellationToken ct)
  {
    var subscribe = new LiveSubscribeMessage { CameraId = cameraId, Profile = profile };
    var payload = MessagePackSerializer.Serialize(subscribe, ProtocolSerializer.Options);
    var stream = await _tunnel.OpenStreamAsync(StreamTypes.LiveSubscribe, payload, ct);

    var feed = new VideoFeed(stream, cameraId, profile);
    feed.Start(CancellationToken.None);

    lock (_lock)
      _subscriptions.Add(new ActiveSubscription(cameraId, profile, feed));

    return feed;
  }

  public async Task UnsubscribeAsync(VideoFeed feed, CancellationToken ct)
  {
    lock (_lock)
      _subscriptions.RemoveAll(s => s.Feed == feed);

    await feed.DisposeAsync();
  }

  private void OnStateChanged(ConnectionState state)
  {
    if (state != ConnectionState.Connected)
      return;

    List<ActiveSubscription> stale;
    lock (_lock)
    {
      stale = [.. _subscriptions];
      _subscriptions.Clear();
    }

    _reconnectCts?.Cancel();
    _reconnectCts?.Dispose();
    _reconnectCts = new CancellationTokenSource();
    _ = ReconnectAsync(stale, _reconnectCts.Token);
  }

  private async Task ReconnectAsync(List<ActiveSubscription> stale, CancellationToken ct)
  {
    try
    {
      foreach (var sub in stale)
      {
        if (ct.IsCancellationRequested) return;
        try { await sub.Feed.DisposeAsync(); }
        catch (Exception ex)
        {
          _logger.LogDebug(ex, "Failed to dispose stale feed for camera {CameraId}", sub.CameraId);
        }
        if (ct.IsCancellationRequested) return;
        try
        {
          var newFeed = await SubscribeAsync(sub.CameraId, sub.Profile, ct);
          FeedReplaced?.Invoke(sub.Feed, newFeed);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
          _logger.LogWarning(ex, "Failed to resubscribe to live stream for camera {CameraId}", sub.CameraId);
        }
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Live stream reconnection failed");
    }
  }

  public void Dispose()
  {
    _tunnel.StateChanged -= OnStateChanged;
    _reconnectCts?.Cancel();
    _reconnectCts?.Dispose();
  }

  private sealed record ActiveSubscription(Guid CameraId, string Profile, VideoFeed Feed);
}
