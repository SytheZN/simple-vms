using Shared.Models.Entities;
using Shared.Models.Events;

namespace Server.Core;

public static class SystemEventFactory
{
  public static SystemEvent ClientConnected(
    Guid clientId, string clientName, string remoteAddress, ulong timestamp) =>
    Create("client-connected", ClientSource(clientId), timestamp, new Dictionary<string, string>
    {
      ["clientId"] = clientId.ToString(),
      ["clientName"] = clientName,
      ["remoteAddress"] = remoteAddress
    });

  public static SystemEvent ClientDisconnected(
    Guid clientId, string clientName, ulong timestamp) =>
    Create("client-disconnected", ClientSource(clientId), timestamp, new Dictionary<string, string>
    {
      ["clientId"] = clientId.ToString(),
      ["clientName"] = clientName
    });

  public static SystemEvent ClientEnrolled(
    Guid clientId, string clientName, ulong timestamp) =>
    Create("client-enroll", ClientSource(clientId), timestamp, new Dictionary<string, string>
    {
      ["clientId"] = clientId.ToString(),
      ["clientName"] = clientName
    });

  public static SystemEvent ClientRevoked(
    Guid clientId, string clientName, ulong timestamp) =>
    Create("client-revoke", ClientSource(clientId), timestamp, new Dictionary<string, string>
    {
      ["clientId"] = clientId.ToString(),
      ["clientName"] = clientName
    });

  public static SystemEvent ClientRenamed(
    Guid clientId, string clientName, string previousName, ulong timestamp) =>
    Create("client-rename", ClientSource(clientId), timestamp, new Dictionary<string, string>
    {
      ["clientId"] = clientId.ToString(),
      ["clientName"] = clientName,
      ["previousName"] = previousName
    });

  public static SystemEvent CameraAdded(
    Guid cameraId, string name, string address, ulong timestamp) =>
    Create("camera-added", CameraSource(cameraId), timestamp, new Dictionary<string, string>
    {
      ["cameraId"] = cameraId.ToString(),
      ["name"] = name,
      ["address"] = address
    });

  public static SystemEvent CameraUpdated(
    Guid cameraId, string name, string? previousName, string? address,
    string? providerId, bool credentialsUpdated, string? rtspPortOverride, ulong timestamp)
  {
    var metadata = new Dictionary<string, string>
    {
      ["cameraId"] = cameraId.ToString(),
      ["name"] = name
    };
    if (previousName != null) metadata["previousName"] = previousName;
    if (address != null) metadata["address"] = address;
    if (providerId != null) metadata["providerId"] = providerId;
    if (credentialsUpdated) metadata["credentialsUpdated"] = "true";
    if (rtspPortOverride != null) metadata["rtspPortOverride"] = rtspPortOverride;
    return Create("camera-updated", CameraSource(cameraId), timestamp, metadata);
  }

  public static SystemEvent CameraReconfigured(
    Guid cameraId, string name,
    IReadOnlyDictionary<string, DiffChange> diff,
    IReadOnlyDictionary<Guid, string> streamProfilesById,
    ulong timestamp)
  {
    var metadata = new Dictionary<string, string>
    {
      ["cameraId"] = cameraId.ToString(),
      ["name"] = name
    };
    var cameraPrefix = $"cameras[{cameraId}].";
    foreach (var (path, change) in diff)
    {
      var label = FriendlyDiffLabel(path, cameraPrefix, streamProfilesById);
      metadata[label] = change.Type switch
      {
        DiffChangeType.Add => change.NewValue != null ? $"added: {change.NewValue}" : "added",
        DiffChangeType.Remove => change.OldValue != null ? $"removed: {change.OldValue}" : "removed",
        DiffChangeType.Update => $"{change.OldValue ?? ""} > {change.NewValue ?? ""}",
        _ => ""
      };
    }
    return Create("camera-reconfigured", CameraSource(cameraId), timestamp, metadata);
  }

  private static string FriendlyDiffLabel(
    string path, string cameraPrefix,
    IReadOnlyDictionary<Guid, string> streamProfilesById)
  {
    var trimmed = path.StartsWith(cameraPrefix, StringComparison.Ordinal)
      ? path[cameraPrefix.Length..]
      : path;

    const string streamsMarker = "streams[";
    var streamsIdx = trimmed.IndexOf(streamsMarker, StringComparison.Ordinal);
    if (streamsIdx < 0) return trimmed;

    var idStart = streamsIdx + streamsMarker.Length;
    var idEnd = trimmed.IndexOf(']', idStart);
    if (idEnd < 0) return trimmed;

    var idText = trimmed[idStart..idEnd];
    if (!Guid.TryParse(idText, out var streamId)) return trimmed;
    if (!streamProfilesById.TryGetValue(streamId, out var profile)) return trimmed;

    var before = trimmed[..streamsIdx];
    var after = trimmed[(idEnd + 1)..];
    var suffix = after.StartsWith('.') ? after[1..] : after;
    return string.IsNullOrEmpty(suffix)
      ? $"{before}stream '{profile}'"
      : $"{before}stream '{profile}'.{suffix}";
  }

  public static SystemEvent CameraRemoved(
    Guid cameraId, string name, ulong timestamp) =>
    Create("camera-removed", CameraSource(cameraId), timestamp, new Dictionary<string, string>
    {
      ["cameraId"] = cameraId.ToString(),
      ["name"] = name
    });

  public static SystemEvent RetentionLowSpacePurge(
    long freeBytesBefore, long freeBytesAfter, long minFreeBytes,
    int purgedSegments, long purgedBytes, ulong timestamp) =>
    Create("retention-low-space-purge", RetentionSource, timestamp, new Dictionary<string, string>
    {
      ["freeBytesBefore"] = freeBytesBefore.ToString(),
      ["freeBytesAfter"] = freeBytesAfter.ToString(),
      ["minFreeBytes"] = minFreeBytes.ToString(),
      ["purgedSegments"] = purgedSegments.ToString(),
      ["purgedBytes"] = purgedBytes.ToString()
    });

  public static SystemEvent RetentionEmergencyStop(
    long freeBytes, long hardFloorBytes, int haltedWriters, ulong timestamp) =>
    Create("retention-emergency-stop", RetentionSource, timestamp, new Dictionary<string, string>
    {
      ["freeBytes"] = freeBytes.ToString(),
      ["hardFloorBytes"] = hardFloorBytes.ToString(),
      ["haltedWriters"] = haltedWriters.ToString()
    });

  public static SystemEvent RetentionRecordingResumed(
    long freeBytes, long minFreeBytes, ulong timestamp) =>
    Create("retention-recording-resumed", RetentionSource, timestamp, new Dictionary<string, string>
    {
      ["freeBytes"] = freeBytes.ToString(),
      ["minFreeBytes"] = minFreeBytes.ToString()
    });

  private const string RetentionSource = "retention";
  private static string ClientSource(Guid clientId) => $"client:{clientId}";
  private static string CameraSource(Guid cameraId) => $"camera:{cameraId}";

  private static SystemEvent Create(
    string type, string source, ulong timestamp, Dictionary<string, string> metadata) =>
    new()
    {
      Id = Guid.NewGuid(),
      Type = type,
      Source = source,
      Timestamp = timestamp,
      Metadata = metadata
    };
}
