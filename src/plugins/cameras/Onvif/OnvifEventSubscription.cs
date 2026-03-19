using System.Runtime.CompilerServices;
using Cameras.Onvif.Services;
using Shared.Models;

namespace Cameras.Onvif;

public sealed class OnvifEventSubscription : IEventSubscription
{
  private readonly EventService _events;
  private readonly string _pullPointUri;
  private readonly Credentials _credentials;
  private readonly Guid _cameraId;
  private readonly DateTimeOffset _terminationTime;
  private bool _disposed;

  public OnvifEventSubscription(
    EventService events,
    string pullPointUri,
    Credentials credentials,
    Guid cameraId,
    DateTimeOffset terminationTime)
  {
    _events = events;
    _pullPointUri = pullPointUri;
    _credentials = credentials;
    _cameraId = cameraId;
    _terminationTime = terminationTime;
  }

  public async IAsyncEnumerable<CameraEvent> ReadEventsAsync(
    [EnumeratorCancellation] CancellationToken ct)
  {
    var renewAt = _terminationTime.AddMinutes(-2);

    while (!ct.IsCancellationRequested && !_disposed)
    {
      if (DateTimeOffset.UtcNow >= renewAt)
      {
        try
        {
          await _events.RenewAsync(_pullPointUri, _credentials, ct);
          renewAt = DateTimeOffset.UtcNow.AddMinutes(8);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
        }
      }

      IReadOnlyList<OnvifNotification> notifications;
      try
      {
        notifications = await _events.PullMessagesAsync(_pullPointUri, _credentials, ct);
      }
      catch (Exception) when (!ct.IsCancellationRequested)
      {
        await Task.Delay(TimeSpan.FromSeconds(5), ct);
        continue;
      }

      foreach (var n in notifications)
      {
        var metadata = new Dictionary<string, string> { ["topic"] = n.Topic };
        if (n.Data != null)
          foreach (var (k, v) in n.Data)
            metadata[k] = v;
        if (n.Source != null)
          foreach (var (k, v) in n.Source)
            metadata[$"source.{k}"] = v;
        if (n.IsActive.HasValue)
          metadata["active"] = n.IsActive.Value.ToString();

        yield return new CameraEvent
        {
          Id = Guid.NewGuid(),
          CameraId = _cameraId,
          Type = n.EventType,
          StartTime = n.Timestamp?.ToUnixMicroseconds()
            ?? DateTimeOffset.UtcNow.ToUnixMicroseconds(),
          Metadata = metadata
        };
      }
    }
  }

  public async ValueTask DisposeAsync()
  {
    if (_disposed) return;
    _disposed = true;

    try
    {
      using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
      await _events.UnsubscribeAsync(_pullPointUri, _credentials, cts.Token);
    }
    catch
    {
    }
  }
}
