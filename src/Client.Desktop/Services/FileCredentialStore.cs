using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Client.Core.Platform;

namespace Client.Desktop.Services;

[ExcludeFromCodeCoverage]
public sealed class FileCredentialStore : ICredentialStore
{
  private static readonly string FilePath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "simplevms", "credentials.bin");

  public async Task<CredentialData?> LoadAsync()
  {
    if (!File.Exists(FilePath)) return null;
    var data = await File.ReadAllBytesAsync(FilePath);
    if (data.Length < 32) return null;

    var salt = data[..16];
    var iv = data[16..32];
    var cipher = data[32..];

    using var aes = CreateAes(salt);
    aes.IV = iv;
    using var decryptor = aes.CreateDecryptor();
    var json = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
    return JsonSerializer.Deserialize(json, CredentialJsonContext.Default.CredentialData);
  }

  public async Task SaveAsync(CredentialData credentialData)
  {
    var json = JsonSerializer.SerializeToUtf8Bytes(credentialData, CredentialJsonContext.Default.CredentialData);
    var salt = RandomNumberGenerator.GetBytes(16);
    using var aes = CreateAes(salt);
    aes.GenerateIV();
    using var encryptor = aes.CreateEncryptor();
    var cipher = encryptor.TransformFinalBlock(json, 0, json.Length);

    var output = new byte[16 + aes.IV.Length + cipher.Length];
    salt.CopyTo(output, 0);
    aes.IV.CopyTo(output, 16);
    cipher.CopyTo(output, 16 + aes.IV.Length);

    Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
    await File.WriteAllBytesAsync(FilePath, output);
  }

  public Task ClearAsync()
  {
    if (File.Exists(FilePath)) File.Delete(FilePath);
    return Task.CompletedTask;
  }

  private static Aes CreateAes(byte[] salt)
  {
    var machineId = GetMachineId();
    var aes = Aes.Create();
    aes.Key = Rfc2898DeriveBytes.Pbkdf2(machineId, salt, 100_000, HashAlgorithmName.SHA256, 32);
    return aes;
  }

  private static byte[] GetMachineId()
  {
    try
    {
      var id = File.ReadAllText("/etc/machine-id").Trim();
      return Encoding.UTF8.GetBytes(id);
    }
    catch
    {
      return Encoding.UTF8.GetBytes(Environment.MachineName);
    }
  }
}
