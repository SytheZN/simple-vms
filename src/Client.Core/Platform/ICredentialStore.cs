namespace Client.Core.Platform;

public interface ICredentialStore
{
  Task<CredentialData?> LoadAsync();
  Task SaveAsync(CredentialData data);
  Task ClearAsync();
}
