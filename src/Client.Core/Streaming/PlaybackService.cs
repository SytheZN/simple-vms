using Client.Core.Tunnel;
using MessagePack;
using Shared.Protocol;

namespace Client.Core.Streaming;

public sealed class PlaybackService : IPlaybackService
{
  private readonly ITunnelService _tunnel;

  public PlaybackService(ITunnelService tunnel)
  {
    _tunnel = tunnel;
  }

  public async Task<VideoFeed> StartAsync(
    Guid cameraId, string profile, ulong from, ulong? to, CancellationToken ct)
  {
    var request = new PlaybackRequestMessage
    {
      CameraId = cameraId,
      Profile = profile,
      From = from,
      To = to
    };
    var payload = MessagePackSerializer.Serialize(request, ProtocolSerializer.Options);
    var stream = await _tunnel.OpenStreamAsync(StreamTypes.Playback, payload, ct);

    var feed = new VideoFeed(stream, cameraId, profile);
    feed.Start(CancellationToken.None);
    return feed;
  }

  public async Task<VideoFeed> SeekAsync(VideoFeed current, ulong timestamp, CancellationToken ct)
  {
    var cameraId = current.CameraId;
    var profile = current.Profile;
    await current.DisposeAsync();
    return await StartAsync(cameraId, profile, timestamp, null, ct);
  }

  public async Task StopAsync(VideoFeed feed, CancellationToken ct)
  {
    await feed.DisposeAsync();
  }
}
