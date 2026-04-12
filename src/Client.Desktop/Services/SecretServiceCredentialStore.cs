using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Client.Core.Platform;

namespace Client.Desktop.Services;

[ExcludeFromCodeCoverage]
public sealed class SecretServiceCredentialStore : ICredentialStore
{
  private const string ServiceName = "SimpleVMS";
  private const string AccountName = "credentials";
  private readonly bool _hasSecretTool;
  private readonly FileCredentialStore _fallback = new();

  public SecretServiceCredentialStore()
  {
    _hasSecretTool = CanRunSecretTool();
  }

  public Task<CredentialData?> LoadAsync() =>
    _hasSecretTool ? LoadFromSecretServiceAsync() : _fallback.LoadAsync();

  public Task SaveAsync(CredentialData data) =>
    _hasSecretTool ? SaveToSecretServiceAsync(data) : _fallback.SaveAsync(data);

  public Task ClearAsync() =>
    _hasSecretTool ? ClearSecretServiceAsync() : _fallback.ClearAsync();

  private async Task<CredentialData?> LoadFromSecretServiceAsync()
  {
    var (exitCode, output) = await RunSecretToolAsync(null, "lookup", "service", ServiceName, "account", AccountName);
    if (exitCode != 0 || string.IsNullOrWhiteSpace(output)) return null;
    return JsonSerializer.Deserialize(output.Trim(), CredentialJsonContext.Default.CredentialData);
  }

  private async Task SaveToSecretServiceAsync(CredentialData data)
  {
    var json = JsonSerializer.Serialize(data, CredentialJsonContext.Default.CredentialData);
    await RunSecretToolAsync(json, "store", "--label", ServiceName, "service", ServiceName, "account", AccountName);
  }

  private async Task ClearSecretServiceAsync()
  {
    await RunSecretToolAsync(null, "clear", "service", ServiceName, "account", AccountName);
  }

  private static async Task<(int ExitCode, string Output)> RunSecretToolAsync(string? stdin, params string[] args)
  {
    var psi = new ProcessStartInfo("secret-tool")
    {
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      RedirectStandardInput = stdin != null,
      UseShellExecute = false,
      CreateNoWindow = true
    };
    foreach (var arg in args) psi.ArgumentList.Add(arg);

    using var proc = Process.Start(psi)!;
    if (stdin != null)
    {
      await proc.StandardInput.WriteAsync(stdin);
      proc.StandardInput.Close();
    }
    var output = await proc.StandardOutput.ReadToEndAsync();
    await proc.WaitForExitAsync();
    return (proc.ExitCode, output);
  }

  private static bool CanRunSecretTool()
  {
    try
    {
      var psi = new ProcessStartInfo("secret-tool", "--version")
      {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
      };
      using var proc = Process.Start(psi);
      proc?.WaitForExit(2000);
      return proc?.ExitCode == 0;
    }
    catch { return false; }
  }
}
