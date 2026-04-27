using Microsoft.Extensions.Logging;
using Shared.Models.Entities;

namespace Server.Core;

public static class StreamHierarchy
{
  private const int MaxDepth = 16;

  public static CameraStream ResolveRootStream(
    CameraStream stream,
    Func<Guid, CameraStream?> lookup,
    ILogger? logger = null)
  {
    var current = stream;
    for (var depth = 0; depth < MaxDepth; depth++)
    {
      if (current.ParentStreamId is not Guid parentId) return current;
      var parent = lookup(parentId);
      if (parent == null)
      {
        logger?.LogWarning(
          "ResolveRootStream: stream {StreamId} has dangling ParentStreamId {ParentId}; using stream itself as root",
          stream.Id, parentId);
        return current;
      }
      current = parent;
    }
    logger?.LogWarning(
      "ResolveRootStream: stream {StreamId} parent chain exceeded {MaxDepth}; using last walked as root",
      stream.Id, MaxDepth);
    return current;
  }
}
