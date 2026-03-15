namespace Shared.Models;

public interface IStreamTypeHandler
{
  ushort StreamType { get; }
  Task HandleAsync(Stream stream, string clientIdentity, CancellationToken ct);
}
