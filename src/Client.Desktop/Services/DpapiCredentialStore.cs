using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Client.Core.Platform;

namespace Client.Desktop.Services;

[ExcludeFromCodeCoverage]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class DpapiCredentialStore : ICredentialStore
{
  private static readonly string FilePath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "SimpleVMS", "credentials.bin");

  public async Task<CredentialData?> LoadAsync()
  {
    if (!File.Exists(FilePath)) return null;
    var encrypted = await File.ReadAllBytesAsync(FilePath);
    var decrypted = System.Security.Cryptography.ProtectedData.Unprotect(
      encrypted, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
    return JsonSerializer.Deserialize(decrypted, CredentialJsonContext.Default.CredentialData);
  }

  public async Task SaveAsync(CredentialData data)
  {
    var json = JsonSerializer.SerializeToUtf8Bytes(data, CredentialJsonContext.Default.CredentialData);
    var encrypted = System.Security.Cryptography.ProtectedData.Protect(
      json, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
    Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
    await File.WriteAllBytesAsync(FilePath, encrypted);
  }

  public Task ClearAsync()
  {
    if (File.Exists(FilePath)) File.Delete(FilePath);
    return Task.CompletedTask;
  }
}
