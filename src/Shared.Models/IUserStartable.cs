namespace Shared.Models;

public interface IUserStartable
{
  Task<OneOf<Success, Error>> UserStartAsync(CancellationToken ct);
  Task<OneOf<Success, Error>> UserStopAsync(CancellationToken ct);
}
