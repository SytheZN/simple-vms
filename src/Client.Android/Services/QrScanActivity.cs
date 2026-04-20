using System.Diagnostics.CodeAnalysis;
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.Activity;
using AndroidX.AppCompat.App;
using AndroidX.Camera.Core;
using AndroidX.Camera.Lifecycle;
using AndroidX.Camera.View;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using Java.Util.Concurrent;
using ZXing;
using ZXing.Common;

namespace Client.Android.Services;

[Activity(
  Label = "Scan QR",
  Theme = "@style/Theme.AppCompat.NoActionBar",
  LaunchMode = LaunchMode.SingleTop,
  ConfigurationChanges =
    ConfigChanges.Orientation |
    ConfigChanges.ScreenSize |
    ConfigChanges.ScreenLayout)]
[ExcludeFromCodeCoverage]
public sealed class QrScanActivity : AppCompatActivity
{
  private const int PermissionRequest = 101;

  private PreviewView? _previewView;
  private ProcessCameraProvider? _provider;
  private IExecutorService? _analyzerExecutor;
  private bool _completed;

  protected override void OnCreate(Bundle? savedInstanceState)
  {
    base.OnCreate(savedInstanceState);

    OnBackPressedDispatcher.AddCallback(this, new BackCallback(() => CompleteAndFinish(null)));

    _previewView = new PreviewView(this);
    var cancel = new Button(this) { Text = "Cancel" };
    cancel.Click += (_, _) => CompleteAndFinish(null);

    var previewStack = new FrameLayout(this);
    _previewView.LayoutParameters = new FrameLayout.LayoutParams(
      FrameLayout.LayoutParams.MatchParent, FrameLayout.LayoutParams.MatchParent);
    var reticle = new ReticleOverlay(this)
    {
      LayoutParameters = new FrameLayout.LayoutParams(
        FrameLayout.LayoutParams.MatchParent, FrameLayout.LayoutParams.MatchParent)
    };
    previewStack.AddView(_previewView);
    previewStack.AddView(reticle);

    var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };
    previewStack.LayoutParameters = new LinearLayout.LayoutParams(
      LinearLayout.LayoutParams.MatchParent, 0, 1f);
    cancel.LayoutParameters = new LinearLayout.LayoutParams(
      LinearLayout.LayoutParams.MatchParent, LinearLayout.LayoutParams.WrapContent);
    layout.AddView(previewStack);
    layout.AddView(cancel);
    SetContentView(layout);

    if (ContextCompat.CheckSelfPermission(this, Manifest.Permission.Camera) == Permission.Granted)
      StartCamera();
    else
      ActivityCompat.RequestPermissions(this, new[] { Manifest.Permission.Camera }, PermissionRequest);
  }

  public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
  {
    base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
    if (requestCode != PermissionRequest) return;
    if (grantResults.Length > 0 && grantResults[0] == Permission.Granted)
      StartCamera();
    else
      CompleteAndFinish(null);
  }


  protected override void OnDestroy()
  {
    base.OnDestroy();
    _provider?.UnbindAll();
    _analyzerExecutor?.Shutdown();
    _analyzerExecutor = null;
    if (!_completed) AndroidQrScannerService.Complete(null);
  }

  private void StartCamera()
  {
    var future = ProcessCameraProvider.GetInstance(this);
    future.AddListener(new CameraReadyRunnable(this, future), ContextCompat.GetMainExecutor(this));
  }

  private void BindUseCases(ProcessCameraProvider provider)
  {
    _provider = provider;
    provider.UnbindAll();

    var preview = new Preview.Builder().Build()!;
    preview.SurfaceProvider = _previewView!.SurfaceProvider;

    var analysisBuilder = new ImageAnalysis.Builder();
    analysisBuilder.SetBackpressureStrategy(ImageAnalysis.StrategyKeepOnlyLatest);
    var analysis = analysisBuilder.Build()!;
    _analyzerExecutor = Executors.NewSingleThreadExecutor()!;
    analysis.SetAnalyzer(_analyzerExecutor, new QrAnalyzer(OnDecoded));

    provider.BindToLifecycle(this, CameraSelector.DefaultBackCamera!, preview, analysis);
  }

  private void OnDecoded(string text)
  {
    RunOnUiThread(() => CompleteAndFinish(text));
  }

  private void CompleteAndFinish(string? result)
  {
    if (_completed) return;
    _completed = true;
    AndroidQrScannerService.Complete(result);
    Finish();
  }

  private sealed class ReticleOverlay : View
  {
    private readonly Paint _scrim;
    private readonly Paint _corner;
    private readonly float _density;

    public ReticleOverlay(Context context) : base(context)
    {
      _density = context.Resources?.DisplayMetrics?.Density ?? 1f;
      _scrim = new Paint { Color = Color.Argb(64, 0, 0, 0) };
      _corner = new Paint
      {
        Color = Color.White,
        StrokeWidth = _density * 3f,
        AntiAlias = true
      };
      _corner.SetStyle(Paint.Style.Stroke);
    }

    protected override void OnDraw(Canvas canvas)
    {
      var w = Width;
      var h = Height;
      var side = (int)(Math.Min(w, h) * 0.7f);
      var left = (w - side) / 2;
      var top = (h - side) / 2;
      var right = left + side;
      var bottom = top + side;

      canvas.DrawRect(0, 0, w, top, _scrim);
      canvas.DrawRect(0, bottom, w, h, _scrim);
      canvas.DrawRect(0, top, left, bottom, _scrim);
      canvas.DrawRect(right, top, w, bottom, _scrim);

      var cornerLen = side / 10f;
      canvas.DrawLine(left, top, left + cornerLen, top, _corner);
      canvas.DrawLine(left, top, left, top + cornerLen, _corner);
      canvas.DrawLine(right - cornerLen, top, right, top, _corner);
      canvas.DrawLine(right, top, right, top + cornerLen, _corner);
      canvas.DrawLine(left, bottom - cornerLen, left, bottom, _corner);
      canvas.DrawLine(left, bottom, left + cornerLen, bottom, _corner);
      canvas.DrawLine(right - cornerLen, bottom, right, bottom, _corner);
      canvas.DrawLine(right, bottom - cornerLen, right, bottom, _corner);
    }
  }

  private sealed class BackCallback : OnBackPressedCallback
  {
    private readonly Action _onBack;
    public BackCallback(Action onBack) : base(true) { _onBack = onBack; }
    public override void HandleOnBackPressed() => _onBack();
  }

  private sealed class CameraReadyRunnable : Java.Lang.Object, Java.Lang.IRunnable
  {
    private readonly QrScanActivity _activity;
    private readonly IFuture _future;

    public CameraReadyRunnable(QrScanActivity activity, IFuture future)
    {
      _activity = activity;
      _future = future;
    }

    public void Run()
    {
      if (_future.Get() is ProcessCameraProvider provider)
        _activity.BindUseCases(provider);
    }
  }

  private sealed class QrAnalyzer : Java.Lang.Object, ImageAnalysis.IAnalyzer
  {
    private readonly Action<string> _onDecoded;
    private readonly BarcodeReaderGeneric _reader;
    private bool _fired;

    public QrAnalyzer(Action<string> onDecoded)
    {
      _onDecoded = onDecoded;
      _reader = new BarcodeReaderGeneric
      {
        Options = new DecodingOptions
        {
          PossibleFormats = [BarcodeFormat.QR_CODE],
          TryHarder = true
        }
      };
    }

    public void Analyze(IImageProxy? image)
    {
      if (image == null) return;
      try
      {
        if (_fired) return;
        var planes = image.GetPlanes();
        if (planes == null || planes.Length == 0) return;
        var plane = planes[0];
        var buffer = plane.Buffer;
        if (buffer == null) return;

        var width = image.Width;
        var height = image.Height;
        var rowStride = plane.RowStride;
        byte[] bytes;
        if (rowStride == width)
        {
          bytes = new byte[buffer.Remaining()];
          buffer.Get(bytes);
        }
        else
        {
          bytes = new byte[width * height];
          var row = new byte[rowStride];
          for (var y = 0; y < height; y++)
          {
            buffer.Get(row, 0, rowStride);
            Array.Copy(row, 0, bytes, y * width, width);
          }
        }

        var source = new PlanarYUVLuminanceSource(
          bytes, width, height, 0, 0, width, height, false);
        var result = _reader.Decode(source);
        if (result?.Text != null)
        {
          _fired = true;
          _onDecoded(result.Text);
        }
      }
      catch (ReaderException)
      {
      }
      finally
      {
        image.Close();
      }
    }
  }
}
