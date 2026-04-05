using Client.Core.Platform;

namespace Tests.Unit.Client.Mocks;

public sealed class MockCredentialStore : ICredentialStore
{
  public CredentialData? Data { get; set; }

  public Task<CredentialData?> LoadAsync() => Task.FromResult(Data);

  public Task SaveAsync(CredentialData data)
  {
    Data = data;
    return Task.CompletedTask;
  }

  public Task ClearAsync()
  {
    Data = null;
    return Task.CompletedTask;
  }
}
