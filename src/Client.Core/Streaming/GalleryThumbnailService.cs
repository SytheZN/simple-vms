using System.Buffers.Binary;
using Avalonia.Media.Imaging;
using Client.Core.ViewModels;
using Microsoft.Extensions.Logging;
using Shared.Api;
using Shared.Models;
using Shared.Protocol;

namespace Client.Core.Streaming;

public sealed class GalleryThumbnailService : IGalleryThumbnails, IDisposable
{
  private const string ThumbnailSuffix = "-thumbnail";
  private const string ThumbnailFormat = "mjpeg";
  private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

  private const int FragmentVersionBytes = 1;
  private const int FragmentTimestampBytes = 8;
  private const int FragmentPayloadLengthBytes = 4;
  private static ReadOnlySpan<byte> FragmentMagic => "MJPG"u8;
  private static readonly int FragmentPayloadLengthOffset =
    FragmentMagic.Length + FragmentVersionBytes + FragmentTimestampBytes;
  private static readonly int FragmentHeaderBytes =
    FragmentPayloadLengthOffset + FragmentPayloadLengthBytes;

  private readonly ILiveStreamService _live;
  private readonly ILogger<GalleryThumbnailService> _logger;
  private readonly Lock _lock = new();
  private readonly Dictionary<Guid, Subscription> _subscriptions = [];
  private bool _visible = true;

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

      feed.OnGop += gop => Publish(subscription, gop.Data.Span);
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

      feed.Start();

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

  private void Publish(Subscription subscription, ReadOnlySpan<byte> fragment)
  {
    var jpeg = Unwrap(fragment);
    if (jpeg.IsEmpty) return;

    if (!Volatile.Read(ref _visible))
    {
      Interlocked.Exchange(ref subscription.LatestWhileHidden, jpeg.ToArray());
      return;
    }

    Interlocked.Exchange(ref subscription.LatestWhileHidden, null);
    DecodeAndPaint(subscription, jpeg.ToArray());
  }

  private void DecodeAndPaint(Subscription subscription, byte[] bytes)
  {
    Bitmap bitmap;
    try
    {
      using var stream = new MemoryStream(bytes);
      bitmap = new Bitmap(stream);
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Discarding undecodable thumbnail for camera {CameraId}", subscription.Tile.Id);
      return;
    }
    RunOnUi(() =>
    {
      if (!Volatile.Read(ref _visible))
      {
        bitmap.Dispose();
        return;
      }
      subscription.Tile.Thumbnail = bitmap;
    });
  }

  public void SetVisible(bool visible)
  {
    if (Volatile.Read(ref _visible) == visible) return;
    Volatile.Write(ref _visible, visible);
    if (!visible) return;

    List<Subscription> subs;
    lock (_lock) subs = [.. _subscriptions.Values];

    foreach (var sub in subs)
    {
      var bytes = Interlocked.Exchange(ref sub.LatestWhileHidden, null);
      if (bytes != null)
        _ = Task.Run(() => DecodeAndPaint(sub, bytes));
    }
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

    var length = BinaryPrimitives.ReadUInt32LittleEndian(fragment[FragmentPayloadLengthOffset..]);
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

    public byte[]? LatestWhileHidden;

    public async Task CancelAsync()
    {
      await _cts.CancelAsync();
      Interlocked.Exchange(ref LatestWhileHidden, null);
    }
  }
}
