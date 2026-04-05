namespace Client.Core.Platform;

public interface IQrScannerService
{
  Task<string?> ScanAsync(CancellationToken ct);
}
