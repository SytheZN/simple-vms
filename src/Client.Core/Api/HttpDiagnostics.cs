namespace Client.Core.Api;

public sealed record HttpDiagnostics(
  string Url,
  int? StatusCode,
  string? RawBody);
