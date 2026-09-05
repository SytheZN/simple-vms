using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Client.Core.Decoding;

namespace Client.Core.Controls;

[ExcludeFromCodeCoverage]
public partial class VideoPlayer : UserControl
{
  public static readonly StyledProperty<MotionOverlay?> MotionOverlayProperty =
    AvaloniaProperty.Register<VideoPlayer, MotionOverlay?>(nameof(MotionOverlay));

  public static readonly StyledProperty<Player?> PlayerProperty =
    AvaloniaProperty.Register<VideoPlayer, Player?>(nameof(Player));

  private readonly VideoSurface _videoSurface;
  private readonly StackPanel _placeholder;
  private readonly Border _motionLayer;
  private readonly Image _motionImage;
  private WriteableBitmap? _motionBitmap;

  private Color? MotionCellColor
  {
    get
    {
      if (Application.Current?.TryGetResource("MotionActiveColor",
            Application.Current.ActualThemeVariant, out var res) == true && res is Color c)
        return c;
      return null;
    }
  }

  public MotionOverlay? MotionOverlay
  {
    get => GetValue(MotionOverlayProperty);
    set => SetValue(MotionOverlayProperty, value);
  }

  public Player? Player
  {
    get => GetValue(PlayerProperty);
    set => SetValue(PlayerProperty, value);
  }

  public VideoPlayer()
  {
    InitializeComponent();
    _videoSurface = this.FindControl<VideoSurface>("VideoSurface")!;
    _placeholder = this.FindControl<StackPanel>("Placeholder")!;
    _motionLayer = this.FindControl<Border>("MotionLayer")!;
    _motionImage = this.FindControl<Image>("MotionImage")!;
  }

  protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
  {
    base.OnPropertyChanged(change);

    if (change.Property == PlayerProperty)
    {
      AttachPlayer(change.GetNewValue<Player?>());
    }
    else if (change.Property == MotionOverlayProperty)
    {
      DetachOverlay(change.GetOldValue<MotionOverlay?>());
      AttachOverlay(change.GetNewValue<MotionOverlay?>());
    }
  }

  private void AttachPlayer(Player? player)
  {
    _videoSurface.SetPlayer(player);
    _placeholder.IsVisible = player == null;
  }

  private void AttachOverlay(MotionOverlay? overlay)
  {
    if (overlay == null) return;
    overlay.FrameChanged += OnMotionFrame;
    Dispatcher.UIThread.Post(() => _motionLayer.IsVisible = true);
  }

  private void DetachOverlay(MotionOverlay? overlay)
  {
    if (overlay == null) return;
    overlay.FrameChanged -= OnMotionFrame;
    Dispatcher.UIThread.Post(() =>
    {
      _motionLayer.IsVisible = false;
      ClearMotionBitmap();
    });
  }

  private void OnMotionFrame(MotionFrame? frame) =>
    Dispatcher.UIThread.Post(() => PaintMotionFrame(frame));

  private void ClearMotionBitmap()
  {
    _motionImage.Source = null;
    _motionBitmap?.Dispose();
    _motionBitmap = null;
  }

  private void PaintMotionFrame(MotionFrame? frame)
  {
    if (frame == null)
    {
      _motionImage.Source = null;
      return;
    }

    var color = MotionCellColor;
    if (color == null) return;

    if (_motionBitmap == null
        || _motionBitmap.PixelSize.Width != frame.Cols
        || _motionBitmap.PixelSize.Height != frame.Rows)
    {
      _motionImage.Source = null;
      _motionBitmap?.Dispose();
      _motionBitmap = new WriteableBitmap(
        new PixelSize(frame.Cols, frame.Rows), new Vector(96, 96),
        PixelFormat.Bgra8888, AlphaFormat.Premul);
    }

    WriteCells(_motionBitmap, frame, color.Value);

    if (!ReferenceEquals(_motionImage.Source, _motionBitmap))
      _motionImage.Source = _motionBitmap;
    _motionImage.InvalidateVisual();
  }

  private static void WriteCells(WriteableBitmap bitmap, MotionFrame frame, Color color)
  {
    using var fb = bitmap.Lock();
    var row = new byte[frame.Cols * 4];
    for (var y = 0; y < frame.Rows; y++)
    {
      for (var x = 0; x < frame.Cols; x++)
      {
        var value = frame.Cells[y * frame.Cols + x];
        var alpha = color.A * value / 255;
        row[x * 4 + 0] = (byte)(color.B * alpha / 255);
        row[x * 4 + 1] = (byte)(color.G * alpha / 255);
        row[x * 4 + 2] = (byte)(color.R * alpha / 255);
        row[x * 4 + 3] = (byte)alpha;
      }
      Marshal.Copy(row, 0, fb.Address + y * fb.RowBytes, row.Length);
    }
  }
}
