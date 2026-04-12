using Client.Core.Platform;

namespace Client.Desktop.Services;

public static class CredentialStoreFactory
{
  public static ICredentialStore Create()
  {
    if (OperatingSystem.IsWindows())
      return new DpapiCredentialStore();
    if (OperatingSystem.IsMacOS())
      return new KeychainCredentialStore();
    return new SecretServiceCredentialStore();
  }
}
