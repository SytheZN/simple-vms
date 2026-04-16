using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using Client.Core.Decoding;
using Client.Core.Streaming;
using Microsoft.Extensions.Logging;
using Shared.Protocol;
using System.Diagnostics.CodeAnalysis;

namespace Client.Core.Controls;

[ExcludeFromCodeCoverage]
public partial class VideoPlayer : UserControl
{
  public static readonly StyledProperty<IVideoFeed?> MotionFeedProperty =
    AvaloniaProperty.Register<VideoPlayer, IVideoFeed?>(nameof(MotionFeed));

  public static readonly StyledProperty<Player?> PlayerProperty =
    AvaloniaProperty.Register<VideoPlayer, Player?>(nameof(Player));

  private readonly VideoSurface _videoSurface;
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

  public IVideoFeed? MotionFeed
  {
    get => GetValue(MotionFeedProperty);
    set => SetValue(MotionFeedProperty, value);
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
    _motionOverlay = this.FindControl<Canvas>("MotionOverlay")!;
  }

  protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
  {
    base.OnPropertyChanged(change);

    if (change.Property == PlayerProperty)
    {
      var p = change.GetNewValue<Player?>();
      p?.RendererLogger.LogDebug("VideoPlayer.PlayerProperty changed -> player set");
      AttachPlayer(p);
    }
    else if (change.Property == DataContextProperty)
    {
      var dc = change.NewValue;
      // Use whatever logger we have access to via current Player (likely null before binding propagates).
      Player?.RendererLogger.LogDebug("VideoPlayer.DataContextProperty changed -> {Type}",
        dc?.GetType().FullName ?? "null");
    }
    else if (change.Property == MotionFeedProperty)
    {
      DetachMotionFeed(change.GetOldValue<IVideoFeed?>());
      AttachMotionFeed(change.GetNewValue<IVideoFeed?>());
    }
  }

  private void AttachPlayer(Player? player)
  {
    player?.RendererLogger.LogDebug("VideoPlayer.AttachPlayer attached");
    _videoSurface.SetPlayer(player);
    _placeholder.IsVisible = player == null;
  }

  private void AttachMotionFeed(IVideoFeed? feed)
  {
    if (feed == null) return;
    Dispatcher.UIThread.Post(() => _motionOverlay.IsVisible = true);
    feed.OnGop += OnMotionGopReceived;
  }

  private void DetachMotionFeed(IVideoFeed? feed)
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
