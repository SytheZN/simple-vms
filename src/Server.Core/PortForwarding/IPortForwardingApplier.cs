using Shared.Models;
using Shared.Models.Dto;

namespace Server.Core.PortForwarding;

public interface IPortForwardingApplier
{
  Task<OneOf<Success, Error>> ApplyAsync(CancellationToken ct);
  PortForwardingStatus GetStatus();
}
