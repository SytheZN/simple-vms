using System.Collections.Concurrent;
using Shared.Models;

namespace Capture.Rtsp;

public sealed partial class RtspPlugin : ICaptureSource
{
  public string Protocol => "rtsp";

  private readonly ConcurrentDictionary<string, RtspSession> _sessions = new();
  private readonly SemaphoreSlim _sessionLock = new(1, 1);

  public async Task<OneOf<IStreamConnection, Error>> ConnectAsync(
    CameraConnectionInfo info, CancellationToken ct)
  {
    try
    {
      var sessionKey = BuildSessionKey(info);

      await _sessionLock.WaitAsync(ct);
      try
      {
        var session = _sessions.GetOrAdd(sessionKey, _ => new RtspSession(
          info.Uri,
          info.Credentials?.GetValueOrDefault("username"),
          info.Credentials?.GetValueOrDefault("password"),
          Logger));

        var track = await session.EnsureTrackAsync("video", ct);
        await session.AddDemandAsync(ct);
        return new RtspTrackConnection(session, track);
      }
      finally
      {
        _sessionLock.Release();
      }
    }
    catch (Exception ex)
    {
      return Error.Create(
        ModuleIds.PluginRtspCapture, 0x0001,
        Result.InternalError,
        $"RTSP connection failed: {ex.Message}");
    }
  }

  private static string BuildSessionKey(CameraConnectionInfo info)
  {
    if (!Uri.TryCreate(info.Uri, UriKind.Absolute, out var uri))
      return info.Uri;
    return $"{uri.Host}:{uri.Port}{uri.AbsolutePath}";
  }
}
