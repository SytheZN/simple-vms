using System.Diagnostics.CodeAnalysis;
using Android.Content;
using Android.Content.PM;
using Client.Core.Platform;

namespace Client.Android.Services;

[ExcludeFromCodeCoverage]
public sealed class AndroidQrScannerService : IQrScannerService
{
  private static readonly object _gate = new();
  private static TaskCompletionSource<string?>? _pending;

  private readonly global::Android.Content.Context _context;

  public AndroidQrScannerService(global::Android.Content.Context context)
  {
    _context = context;
  }

  public bool IsAvailable =>
    _context.PackageManager?.HasSystemFeature(PackageManager.FeatureCamera) == true
    || _context.PackageManager?.HasSystemFeature(PackageManager.FeatureCameraAny) == true;

  public Task<string?> ScanAsync(CancellationToken ct)
  {
    if (!IsAvailable) return Task.FromResult<string?>(null);

    TaskCompletionSource<string?> tcs;
    lock (_gate)
    {
      if (_pending != null) return _pending.Task;
      tcs = new TaskCompletionSource<string?>();
      _pending = tcs;
    }
    ct.Register(() => tcs.TrySetResult(null));

    var intent = new Intent(_context, typeof(QrScanActivity));
    intent.AddFlags(ActivityFlags.NewTask);
    _context.StartActivity(intent);
    return tcs.Task;
  }

  internal static void Complete(string? result)
  {
    lock (_gate)
    {
      _pending?.TrySetResult(result);
      _pending = null;
    }
  }
}
