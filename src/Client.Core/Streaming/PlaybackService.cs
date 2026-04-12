using Client.Core.Tunnel;
using MessagePack;
using Microsoft.Extensions.Logging;
using Shared.Protocol;

namespace Client.Core.Streaming;

public sealed class PlaybackService : IPlaybackService
{
  private readonly ITunnelService _tunnel;
  private readonly ILogger<PlaybackService> _logger;

  public PlaybackService(ITunnelService tunnel, ILogger<PlaybackService> logger)
  {
    _tunnel = tunnel;
    _logger = logger;
  }

  public async Task<VideoFeed> StartAsync(
    Guid cameraId, string profile, ulong from, ulong? to, CancellationToken ct)
  {
    _logger.LogDebug("Starting playback camera={CameraId} profile={Profile} from={From} to={To}",
      cameraId, profile, from, to);
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
    _logger.LogDebug("Seeking playback camera={CameraId} to={Timestamp}", current.CameraId, timestamp);
    var cameraId = current.CameraId;
    var profile = current.Profile;
    await current.DisposeAsync();
    return await StartAsync(cameraId, profile, timestamp, null, ct);
  }

  public async Task StopAsync(VideoFeed feed, CancellationToken ct)
  {
    _logger.LogDebug("Stopping playback camera={CameraId}", feed.CameraId);
    await feed.DisposeAsync();
  }
}
