using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Client.Core.Streaming;
using Shared.Protocol;
using System.Diagnostics.CodeAnalysis;

namespace Client.Core.Controls;

[ExcludeFromCodeCoverage]
public partial class VideoPlayer : UserControl
{
  public static readonly StyledProperty<VideoFeed?> FeedProperty =
    AvaloniaProperty.Register<VideoPlayer, VideoFeed?>(nameof(Feed));

  public static readonly StyledProperty<VideoFeed?> MotionFeedProperty =
    AvaloniaProperty.Register<VideoPlayer, VideoFeed?>(nameof(MotionFeed));

  private readonly Image _frameImage;
  private readonly StackPanel _placeholder;
  private readonly Canvas _motionOverlay;

  private ISolidColorBrush? MotionCellBrush
  {
    get
    {
      if (Application.Current?.TryGetResource("MotionBrush",
            Application.Current.ActualThemeVariant, out var res) == true)
        return res as ISolidColorBrush;
      return null;
    }
  }

  public VideoFeed? Feed
  {
    get => GetValue(FeedProperty);
    set => SetValue(FeedProperty, value);
  }

  public VideoFeed? MotionFeed
  {
    get => GetValue(MotionFeedProperty);
    set => SetValue(MotionFeedProperty, value);
  }

  public WriteableBitmap? CurrentFrame
  {
    get => _frameImage.Source as WriteableBitmap;
    set
    {
      _frameImage.Source = value;
      _frameImage.IsVisible = value != null;
      _placeholder.IsVisible = value == null;
    }
  }

  public VideoPlayer()
  {
    InitializeComponent();
    _frameImage = this.FindControl<Image>("FrameImage")!;
    _placeholder = this.FindControl<StackPanel>("Placeholder")!;
    _motionOverlay = this.FindControl<Canvas>("MotionOverlay")!;
  }

  protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
  {
    base.OnPropertyChanged(change);

    if (change.Property == MotionFeedProperty)
    {
      DetachMotionFeed(change.GetOldValue<VideoFeed?>());
      AttachMotionFeed(change.GetNewValue<VideoFeed?>());
    }
  }

  private void AttachMotionFeed(VideoFeed? feed)
  {
    if (feed == null) return;
    Dispatcher.UIThread.Post(() => _motionOverlay.IsVisible = true);
    feed.OnGop += OnMotionGopReceived;
  }

  private void DetachMotionFeed(VideoFeed? feed)
  {
    if (feed == null) return;
    feed.OnGop -= OnMotionGopReceived;
    Dispatcher.UIThread.Post(() =>
    {
      _motionOverlay.IsVisible = false;
      _motionOverlay.Children.Clear();
    });
  }

  private void OnMotionGopReceived(GopMessage gop)
  {
    Dispatcher.UIThread.Post(() => RenderMotionOverlay(gop.Data));
  }

  private void RenderMotionOverlay(ReadOnlyMemory<byte> data)
  {
    _motionOverlay.Children.Clear();

    var brush = MotionCellBrush;
    if (brush == null || data.Length < 2 || Bounds.Width == 0 || Bounds.Height == 0)
      return;

    var span = data.Span;
    var cols = span[0];
    var rows = span[1];
    if (cols == 0 || rows == 0) return;

    var cellWidth = Bounds.Width / cols;
    var cellHeight = Bounds.Height / rows;

    for (var row = 0; row < rows; row++)
    {
      for (var col = 0; col < cols; col++)
      {
        var cellIndex = 2 + row * cols + col;
        if (cellIndex >= span.Length) return;
        if (span[cellIndex] == 0) continue;

        var rect = new Rectangle
        {
          Width = cellWidth,
          Height = cellHeight,
          Fill = brush,
          IsHitTestVisible = false
        };
        Canvas.SetLeft(rect, col * cellWidth);
        Canvas.SetTop(rect, row * cellHeight);
        _motionOverlay.Children.Add(rect);
      }
    }
  }
}
