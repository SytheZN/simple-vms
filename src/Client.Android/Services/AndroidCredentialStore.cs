using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using Android.Security.Keystore;
using Client.Core.Platform;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using Microsoft.Extensions.Logging;

namespace Client.Android.Services;

[ExcludeFromCodeCoverage]
public sealed class AndroidCredentialStore : ICredentialStore
{
  private const string PrefsName = "simplevms.credentials";
  private const string KeyAlias = "simplevms.credentials.key";
  private const string PayloadKey = "payload";
  private const string IvKey = "iv";
  private const string KeystoreName = "AndroidKeyStore";
  private const string Transform = "AES/GCM/NoPadding";
  private const int GcmTagBits = 128;

  private readonly global::Android.Content.Context _context;
  private readonly ILogger<AndroidCredentialStore> _logger;

  public AndroidCredentialStore(global::Android.Content.Context context, ILogger<AndroidCredentialStore> logger)
  {
    _context = context;
    _logger = logger;
  }

  public Task<CredentialData?> LoadAsync()
  {
    var prefs = _context.GetSharedPreferences(PrefsName, global::Android.Content.FileCreationMode.Private)!;
    var cipherB64 = prefs.GetString(PayloadKey, null);
    var ivB64 = prefs.GetString(IvKey, null);
    if (string.IsNullOrEmpty(cipherB64) || string.IsNullOrEmpty(ivB64))
      return Task.FromResult<CredentialData?>(null);

    try
    {
      var ciphertext = Convert.FromBase64String(cipherB64);
      var iv = Convert.FromBase64String(ivB64);
      var cipher = Cipher.GetInstance(Transform)!;
      cipher.Init(CipherMode.DecryptMode, GetOrCreateKey(), new GCMParameterSpec(GcmTagBits, iv));
      var plain = cipher.DoFinal(ciphertext)!;
      return Task.FromResult(JsonSerializer.Deserialize(plain, CredentialJsonContext.Default.CredentialData));
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to decrypt stored credentials; treating as absent");
      return Task.FromResult<CredentialData?>(null);
    }
  }

  public Task SaveAsync(CredentialData data)
  {
    var plain = JsonSerializer.SerializeToUtf8Bytes(data, CredentialJsonContext.Default.CredentialData);

    var cipher = Cipher.GetInstance(Transform)!;
    cipher.Init(CipherMode.EncryptMode, GetOrCreateKey());
    var iv = cipher.GetIV()!;
    var ciphertext = cipher.DoFinal(plain)!;

    using var editor = _context.GetSharedPreferences(PrefsName, global::Android.Content.FileCreationMode.Private)!.Edit()!;
    editor.PutString(PayloadKey, Convert.ToBase64String(ciphertext));
    editor.PutString(IvKey, Convert.ToBase64String(iv));
    editor.Apply();
    return Task.CompletedTask;
  }

  public Task ClearAsync()
  {
    using var editor = _context.GetSharedPreferences(PrefsName, global::Android.Content.FileCreationMode.Private)!.Edit()!;
    editor.Remove(PayloadKey);
    editor.Remove(IvKey);
    editor.Apply();

    var keystore = KeyStore.GetInstance(KeystoreName)!;
    keystore.Load(null);
    if (keystore.ContainsAlias(KeyAlias))
      keystore.DeleteEntry(KeyAlias);
    return Task.CompletedTask;
  }

  private static IKey GetOrCreateKey()
  {
    var keystore = KeyStore.GetInstance(KeystoreName)!;
    keystore.Load(null);
    if (keystore.ContainsAlias(KeyAlias))
      return keystore.GetKey(KeyAlias, null)!;

    var generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, KeystoreName)!;
    var specBuilder = new KeyGenParameterSpec.Builder(
      KeyAlias,
      KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt);
    specBuilder.SetBlockModes(KeyProperties.BlockModeGcm);
    specBuilder.SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone);
    specBuilder.SetKeySize(256);
    generator.Init(specBuilder.Build());
    return generator.GenerateKey()!;
  }
}
