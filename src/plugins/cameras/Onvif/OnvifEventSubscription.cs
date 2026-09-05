using System.Runtime.CompilerServices;
using Cameras.Onvif.Services;
using Cameras.Onvif.Soap;
using Microsoft.Extensions.Logging;
using Shared.Models;
using Shared.Models.Entities;

namespace Cameras.Onvif;

public sealed class OnvifEventSubscription : IEventSubscription
{
  private const int MaxConsecutiveContinuingFaults = 5;
  private static readonly TimeSpan ContinuingFaultRetryDelay = TimeSpan.FromSeconds(1);

  private readonly EventService _events;
  private readonly string _eventsUri;
  private readonly Credentials _credentials;
  private readonly Guid _cameraId;
  private readonly ILogger _logger;
  private string _pullPointUri;
  private DateTimeOffset _terminationTime;
  private bool _disposed;

  public OnvifEventSubscription(
    EventService events,
    string eventsUri,
    string pullPointUri,
    Credentials credentials,
    Guid cameraId,
    DateTimeOffset terminationTime,
    ILogger logger)
  {
    _events = events;
    _eventsUri = eventsUri;
    _pullPointUri = pullPointUri;
    _credentials = credentials;
    _cameraId = cameraId;
    _terminationTime = terminationTime;
    _logger = logger;
  }

  public async IAsyncEnumerable<CameraEvent> ReadEventsAsync(
    [EnumeratorCancellation] CancellationToken ct)
  {
    var renewAt = ComputeRenewAt(_terminationTime);
    var consecutiveContinuingFaults = 0;

    while (!ct.IsCancellationRequested && !_disposed)
    {
      if (DateTimeOffset.UtcNow >= renewAt)
      {
        try
        {
          await _events.RenewAsync(_pullPointUri, _credentials, ct);
          renewAt = ComputeRenewAt(_terminationTime);
        }
        catch (SoapFaultException ex) when (!ct.IsCancellationRequested)
        {
          _logger.LogWarning(ex, "Renew failed for camera {CameraId}; recreating pullpoint", _cameraId);
          if (!await TryRecreateAsync(ct))
          {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            continue;
          }
          renewAt = ComputeRenewAt(_terminationTime);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
          renewAt = DateTimeOffset.UtcNow.AddSeconds(30);
        }
      }

      IReadOnlyList<OnvifNotification> notifications;
      try
      {
        notifications = await _events.PullMessagesAsync(_pullPointUri, _credentials, ct);
        consecutiveContinuingFaults = 0;
      }
      catch (SoapFaultException ex) when (!ct.IsCancellationRequested
        && IsPullInProgressFault(ex)
        && consecutiveContinuingFaults < MaxConsecutiveContinuingFaults)
      {
        consecutiveContinuingFaults++;
        _logger.LogDebug(
          "PullMessages for camera {CameraId} reported a prior pull still in progress; waiting to retry (attempt {Attempt})",
          _cameraId, consecutiveContinuingFaults);
        await Task.Delay(ContinuingFaultRetryDelay, ct);
        continue;
      }
      catch (SoapFaultException ex) when (!ct.IsCancellationRequested)
      {
        consecutiveContinuingFaults = 0;
        _logger.LogWarning(ex, "PullMessages failed for camera {CameraId}; recreating pullpoint", _cameraId);
        if (!await TryRecreateAsync(ct))
          await Task.Delay(TimeSpan.FromSeconds(5), ct);
        renewAt = ComputeRenewAt(_terminationTime);
        continue;
      }
      catch (Exception) when (!ct.IsCancellationRequested)
      {
        consecutiveContinuingFaults = 0;
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
        if (n.PropertyOperation != null)
          metadata["propertyOperation"] = n.PropertyOperation;

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

  private async Task<bool> TryRecreateAsync(CancellationToken ct)
  {
    try
    {
      var info = await _events.CreatePullPointAsync(_eventsUri, _credentials, ct);
      _pullPointUri = info.SubscriptionUri;
      _terminationTime = info.TerminationTime;
      return true;
    }
    catch (Exception ex) when (!ct.IsCancellationRequested)
    {
      _logger.LogWarning(ex, "Recreate pullpoint failed for camera {CameraId}", _cameraId);
      return false;
    }
  }

  private static bool IsPullInProgressFault(SoapFaultException ex) =>
    ex.Message.Contains("continuing", StringComparison.OrdinalIgnoreCase)
      || ex.Message.Contains("in progress", StringComparison.OrdinalIgnoreCase);

  private static DateTimeOffset ComputeRenewAt(DateTimeOffset terminationTime)
  {
    var window = terminationTime - DateTimeOffset.UtcNow;
    if (window <= TimeSpan.FromMinutes(2))
      return DateTimeOffset.UtcNow.Add(TimeSpan.FromTicks(window.Ticks / 2));
    return terminationTime - TimeSpan.FromMinutes(2);
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
