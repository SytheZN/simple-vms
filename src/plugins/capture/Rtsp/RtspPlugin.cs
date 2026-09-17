using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Models;
using Shared.Models.Events;

namespace Capture.Rtsp;

public sealed partial class RtspPlugin : IPlugin
{
  private CancellationTokenSource? _eventCts;

  internal ILogger Logger { get; private set; } = NullLogger.Instance;
  internal IEventBus? EventBus { get; private set; }

  public PluginMetadata Metadata { get; } = new()
  {
    Id = "rtsp",
    Name = "RTSP Capture",
    Version = "1.0.0",
    Description = "RTSP/TCP interleaved capture source"
  };

  public OneOf<Success, Error> Initialize(PluginContext context)
  {
    Logger = context.LoggerFactory.CreateLogger("Connection");
    EventBus = context.EventBus;
    return new Success();
  }

  public Task<OneOf<Success, Error>> StartAsync(CancellationToken ct)
  {
    if (EventBus != null)
    {
      _eventCts = new CancellationTokenSource();
      _ = ForgetSessionsOfRemovedCamerasAsync(EventBus, _eventCts.Token);
    }
    return Task.FromResult<OneOf<Success, Error>>(new Success());
  }

  public async Task<OneOf<Success, Error>> StopAsync(CancellationToken ct)
  {
    _eventCts?.Cancel();
    _eventCts?.Dispose();
    _eventCts = null;

    await ForgetSessionsAsync(_ => true);
    return new Success();
  }

  private async Task ForgetSessionsOfRemovedCamerasAsync(IEventBus eventBus, CancellationToken ct)
  {
    await foreach (var removed in eventBus.SubscribeAsync<CameraRemoved>(ct))
      await ForgetSessionsAsync(session => session.CameraId == removed.CameraId);
  }
}
