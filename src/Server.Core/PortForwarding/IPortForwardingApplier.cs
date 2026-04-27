using Shared.Models;
using Shared.Api;

namespace Server.Core.PortForwarding;

public interface IPortForwardingApplier
{
  Task<OneOf<Success, Error>> ApplyAsync(CancellationToken ct);
  PortForwardingStatusDto GetStatus();
}
