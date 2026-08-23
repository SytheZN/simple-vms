using System.Buffers.Binary;
using Avalonia.Media.Imaging;
using Client.Core.ViewModels;
using Microsoft.Extensions.Logging;
using Shared.Api;
using Shared.Models;
using Shared.Protocol;

namespace Client.Core.Streaming;

/// <summary>
/// Keeps the gallery's tiles supplied with thumbnails. Holding a subscription is what starts the
/// analyzer producing them, so the streams live exactly as long as the gallery is on screen.
/// </summary>
public sealed class GalleryThumbnailService : IGalleryThumbnails, IDisposable
{
  private const string ThumbnailSuffix = "-thumbnail";
  private const string ThumbnailFormat = "mjpeg";
  private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

  // MJPG magic, version, 8-byte timestamp, 4-byte payload length.
  private const int FragmentHeaderBytes = 17;
  private static ReadOnlySpan<byte> FragmentMagic => "MJPG"u8;

  private readonly ILiveStreamService _live;
  private readonly ILogger<GalleryThumbnailService> _logger;
  private readonly Lock _lock = new();
  private readonly Dictionary<Guid, Subscription> _subscriptions = [];

  public GalleryThumbnailService(ILiveStreamService live, ILogger<GalleryThumbnailService> logger)
  {
    _live = live;
    _logger = logger;
  }

  public void Sync(IReadOnlyList<CameraTile> tiles)
  {
    var wanted = new Dictionary<Guid, (CameraTile Tile, List<string> Profiles)>();
    foreach (var tile in tiles)
    {
      var profiles = ThumbnailProfiles(tile.Camera);
      if (profiles.Count > 0)
        wanted[tile.Id] = (tile, profiles);
    }

    List<Subscription> dropped = [];
    List<Subscription> started = [];

    lock (_lock)
    {
      foreach (var (cameraId, existing) in _subscriptions.ToList())
      {
        if (wanted.TryGetValue(cameraId, out var match)
          && match.Profiles.SequenceEqual(existing.Profiles))
          continue;

        _subscriptions.Remove(cameraId);
        dropped.Add(existing);
      }

      foreach (var (cameraId, (tile, profiles)) in wanted)
      {
        if (_subscriptions.ContainsKey(cameraId)) continue;
        var subscription = new Subscription(tile, profiles);
        _subscriptions[cameraId] = subscription;
        started.Add(subscription);
      }
    }

    foreach (var subscription in dropped)
      _ = StopAsync(subscription);

    foreach (var subscription in started)
      _ = RunAsync(subscription);
  }

  public void Stop()
  {
    List<Subscription> all;
    lock (_lock)
    {
      all = [.. _subscriptions.Values];
      _subscriptions.Clear();
    }

    foreach (var subscription in all)
      _ = StopAsync(subscription);
  }

  internal static List<string> ThumbnailProfiles(CameraDto camera) =>
    [.. camera.Streams
      .Where(s => s.Kind == StreamKind.Metadata
        && s.FormatId == ThumbnailFormat
        && s.Profile.EndsWith(ThumbnailSuffix, StringComparison.Ordinal))
      .Select(s => s.Profile)];

  /// <summary>
  /// A profile the server cannot serve right now is not a profile that does not exist, so the
  /// candidates are tried in turn and then retried from the top rather than given up on.
  /// </summary>
  private async Task RunAsync(Subscription subscription)
  {
    var candidate = 0;

    while (!subscription.Token.IsCancellationRequested)
    {
      var outcome = await AttachAsync(subscription, subscription.Profiles[candidate]);
      if (subscription.Token.IsCancellationRequested) return;

      ClearThumbnail(subscription.Tile);

      if (outcome == Outcome.Unavailable && candidate + 1 < subscription.Profiles.Count)
      {
        candidate++;
        continue;
      }

      candidate = 0;
      await DelayAsync(subscription);
    }
  }

  private async Task<Outcome> AttachAsync(Subscription subscription, string profile)
  {
    IVideoFeed? feed = null;
    var finished = new TaskCompletionSource<Outcome>(
      TaskCreationOptions.RunContinuationsAsynchronously);

    try
    {
      feed = await _live.SubscribeAsync(subscription.Tile.Id, profile, subscription.Token);

      feed.OnGop += gop => Publish(subscription.Tile, gop.Data.Span);
      feed.OnStatus += status =>
      {
        if (status == StreamStatus.Error)
          finished.TrySetResult(Outcome.Unavailable);
        else if (status == StreamStatus.Ended)
          finished.TrySetResult(Outcome.Ended);
      };
      feed.OnCompleted += () => finished.TrySetResult(Outcome.Ended);

      using var registration = subscription.Token.Register(
        () => finished.TrySetResult(Outcome.Ended));

      return await finished.Task;
    }
    catch (OperationCanceledException)
    {
      return Outcome.Ended;
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Thumbnail stream '{Profile}' failed for camera {CameraId}",
        profile, subscription.Tile.Id);
      return Outcome.Ended;
    }
    finally
    {
      if (feed != null)
      {
        try { await _live.UnsubscribeAsync(feed, CancellationToken.None); }
        catch (Exception ex)
        {
          _logger.LogDebug(ex, "Failed to release thumbnail stream for camera {CameraId}",
            subscription.Tile.Id);
        }
      }
    }
  }

  private void Publish(CameraTile tile, ReadOnlySpan<byte> fragment)
  {
    var jpeg = Unwrap(fragment);
    if (jpeg.IsEmpty) return;

    Bitmap bitmap;
    try
    {
      using var stream = new MemoryStream(jpeg.ToArray());
      bitmap = new Bitmap(stream);
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Discarding undecodable thumbnail for camera {CameraId}", tile.Id);
      return;
    }

    RunOnUi(() => tile.Thumbnail = bitmap);
  }

  private static void ClearThumbnail(CameraTile tile) => RunOnUi(() => tile.Thumbnail = null);

  private static void RunOnUi(Action action)
  {
    if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
      action();
    else
      Avalonia.Threading.Dispatcher.UIThread.Post(action);
  }

  internal static ReadOnlySpan<byte> Unwrap(ReadOnlySpan<byte> fragment)
  {
    if (fragment.Length <= FragmentHeaderBytes) return default;
    if (!fragment[..FragmentMagic.Length].SequenceEqual(FragmentMagic)) return default;

    var length = BinaryPrimitives.ReadUInt32LittleEndian(fragment[13..]);
    if (length == 0 || FragmentHeaderBytes + length > fragment.Length) return default;

    return fragment.Slice(FragmentHeaderBytes, (int)length);
  }

  private static async Task DelayAsync(Subscription subscription) =>
    await Task.Delay(RetryDelay, subscription.Token)
      .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

  private static async Task StopAsync(Subscription subscription)
  {
    await subscription.CancelAsync();
    ClearThumbnail(subscription.Tile);
  }

  public void Dispose() => Stop();

  private enum Outcome
  {
    Unavailable,
    Ended
  }

  private sealed class Subscription(CameraTile tile, List<string> profiles)
  {
    private readonly CancellationTokenSource _cts = new();

    public CameraTile Tile => tile;
    public List<string> Profiles => profiles;
    public CancellationToken Token => _cts.Token;

    public Task CancelAsync() => _cts.CancelAsync();
  }
}
