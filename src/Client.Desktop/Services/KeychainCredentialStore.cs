using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using Client.Core.Platform;

namespace Client.Desktop.Services;

[ExcludeFromCodeCoverage]
[System.Runtime.Versioning.SupportedOSPlatform("macos")]
public sealed class KeychainCredentialStore : ICredentialStore
{
  private const string ServiceName = "SimpleVMS";
  private const string AccountName = "credentials";

  public async Task<CredentialData?> LoadAsync()
  {
    var (exitCode, output) = await RunSecurityAsync(
      "find-generic-password", "-s", ServiceName, "-a", AccountName, "-w");
    if (exitCode != 0) return null;

    var json = Convert.FromBase64String(output.Trim());
    return JsonSerializer.Deserialize(json, CredentialJsonContext.Default.CredentialData);
  }

  public async Task SaveAsync(CredentialData data)
  {
    var json = JsonSerializer.SerializeToUtf8Bytes(data, CredentialJsonContext.Default.CredentialData);
    var b64 = Convert.ToBase64String(json);
    await RunSecurityAsync(
      "add-generic-password", "-U", "-s", ServiceName, "-a", AccountName, "-w", b64);
  }

  public async Task ClearAsync()
  {
    await RunSecurityAsync("delete-generic-password", "-s", ServiceName, "-a", AccountName);
  }

  private static async Task<(int ExitCode, string Output)> RunSecurityAsync(params string[] args)
  {
    var psi = new ProcessStartInfo("security")
    {
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };
    foreach (var arg in args) psi.ArgumentList.Add(arg);

    using var proc = Process.Start(psi)!;
    var output = await proc.StandardOutput.ReadToEndAsync();
    await proc.WaitForExitAsync();
    return (proc.ExitCode, output);
  }
}
