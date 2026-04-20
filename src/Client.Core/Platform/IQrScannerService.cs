namespace Client.Core.Platform;

public interface IQrScannerService
{
  bool IsAvailable { get; }
  Task<string?> ScanAsync(CancellationToken ct);
}
