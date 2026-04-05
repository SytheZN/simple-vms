namespace Client.Core.Platform;

public sealed record CredentialData(
  string CaCert,
  string ClientCert,
  string ClientKey,
  string[] Addresses,
  Guid ClientId);
