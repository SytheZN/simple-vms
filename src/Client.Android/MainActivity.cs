using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;
using AndroidX.Activity;
using AndroidX.Core.View;
using Avalonia.Android;
using Client.Android.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;

namespace Client.Android;

[Activity(
  Label = "SimpleVMS",
  Icon = "@drawable/icon",
  Banner = "@drawable/banner",
  Theme = "@style/AppTheme",
  MainLauncher = true,
  LaunchMode = LaunchMode.SingleTask,
  ConfigurationChanges =
    ConfigChanges.Orientation |
    ConfigChanges.ScreenSize |
    ConfigChanges.UiMode |
    ConfigChanges.ScreenLayout |
    ConfigChanges.SmallestScreenSize |
    ConfigChanges.Density |
    ConfigChanges.Keyboard |
    ConfigChanges.KeyboardHidden |
    ConfigChanges.Navigation)]
[IntentFilter(
  new[] { Intent.ActionMain },
  Categories = new[] { Intent.CategoryLeanbackLauncher })]
public sealed class MainActivity : AvaloniaMainActivity
{
  private MainShellViewModel? _shell;

  protected override void OnCreate(Bundle? savedInstanceState)
  {
    base.OnCreate(savedInstanceState);
    OnBackPressedDispatcher.AddCallback(this, new BackCallback(this));

    _shell = (Avalonia.Application.Current as AndroidApp)?.Services?
      .GetService<MainShellViewModel>();
    if (_shell != null)
    {
      _shell.PropertyChanged += OnShellPropertyChanged;
      ApplyImmersive(_shell.IsFullscreen);
    }
  }

  protected override void OnResume()
  {
    base.OnResume();
    if (_shell != null) ApplyImmersive(_shell.IsFullscreen);
  }

  public override void OnWindowFocusChanged(bool hasFocus)
  {
    base.OnWindowFocusChanged(hasFocus);
    if (hasFocus && _shell != null) ApplyImmersive(_shell.IsFullscreen);
  }

  protected override void OnDestroy()
  {
    var finishing = IsFinishing;
    if (_shell != null) _shell.PropertyChanged -= OnShellPropertyChanged;
    base.OnDestroy();
    if (finishing)
      global::Android.OS.Process.KillProcess(global::Android.OS.Process.MyPid());
  }

  private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
  {
    if (e.PropertyName == nameof(MainShellViewModel.IsFullscreen) && _shell != null)
      ApplyImmersive(_shell.IsFullscreen);
  }

  private void ApplyImmersive(bool fs)
  {
    var window = Window;
    if (window == null) return;

    RequestedOrientation = fs && _shell?.IsCompactLayout == true
      ? ScreenOrientation.SensorLandscape
      : ScreenOrientation.Unspecified;

    WindowCompat.SetDecorFitsSystemWindows(window, !fs);

    if (OperatingSystem.IsAndroidVersionAtLeast(28))
    {
      var attrs = window.Attributes;
      if (attrs != null)
      {
        attrs.LayoutInDisplayCutoutMode = fs && OperatingSystem.IsAndroidVersionAtLeast(30)
          ? LayoutInDisplayCutoutMode.Always
          : fs
            ? LayoutInDisplayCutoutMode.ShortEdges
            : LayoutInDisplayCutoutMode.Default;
        window.Attributes = attrs;
      }
    }

    var windowBg = Resources != null && Theme != null
      ? new global::Android.Graphics.Color(Resources.GetColor(Resource.Color.windowBackground, Theme))
      : global::Android.Graphics.Color.ParseColor("#0B0D10");
    window.SetBackgroundDrawable(new ColorDrawable(
      fs ? global::Android.Graphics.Color.Black : windowBg));

    var controller = WindowCompat.GetInsetsController(window, window.DecorView);
    if (controller == null) return;
    var bars = WindowInsetsCompat.Type.SystemBars();
    if (fs)
    {
      controller.SystemBarsBehavior = WindowInsetsControllerCompat.BehaviorShowTransientBarsBySwipe;
      controller.Hide(bars);
    }
    else
    {
      controller.Show(bars);
    }
  }

  private sealed class BackCallback : OnBackPressedCallback
  {
    private readonly MainActivity _activity;

    public BackCallback(MainActivity activity) : base(true)
    {
      _activity = activity;
    }

    public override void HandleOnBackPressed()
    {
      var shell = (Avalonia.Application.Current as AndroidApp)?.Services?
        .GetService<MainShellViewModel>();

      if (shell is { IsFullscreen: true })
      {
        shell.IsFullscreen = false;
        return;
      }

      if (shell is { IsEnrolled: true } &&
          shell.CurrentView != MainShellViewModel.ViewKind.Gallery)
      {
        shell.GoBack();
        return;
      }

      _activity.MoveTaskToBack(true);
    }
  }
}
