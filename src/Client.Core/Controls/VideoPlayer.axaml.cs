using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using Client.Core.Streaming;
using LibVLCSharp.Shared;
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

  private readonly Canvas _motionOverlay;
  private static readonly ISolidColorBrush MotionCellBrush = new SolidColorBrush(Color.FromArgb(90, 255, 180, 0));

  private LibVLC? _libVlc;
  private MediaPlayer? _player;
  private FeedMediaInput? _mediaInput;

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

  public VideoPlayer()
  {
    InitializeComponent();
    _motionOverlay = this.FindControl<Canvas>("MotionOverlay")!;
  }

  protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
  {
    base.OnDetachedFromVisualTree(e);
    DetachFeed(Feed);
    DetachMotionFeed(MotionFeed);
    _libVlc?.Dispose();
    _libVlc = null;
  }

  protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
  {
    base.OnPropertyChanged(change);

    if (change.Property == FeedProperty)
    {
      DetachFeed(change.GetOldValue<VideoFeed?>());
      AttachFeed(change.GetNewValue<VideoFeed?>());
    }
    else if (change.Property == MotionFeedProperty)
    {
      DetachMotionFeed(change.GetOldValue<VideoFeed?>());
      AttachMotionFeed(change.GetNewValue<VideoFeed?>());
    }
  }

  private void AttachFeed(VideoFeed? feed)
  {
    if (feed == null) return;

    _libVlc ??= new LibVLC();
    _mediaInput = new FeedMediaInput();
    _player = new MediaPlayer(_libVlc);

    feed.OnInit += _mediaInput.OnInit;
    feed.OnGop += _mediaInput.OnGop;

    using var media = new Media(_libVlc, _mediaInput);
    var videoView = this.FindControl<LibVLCSharp.Avalonia.VideoView>("VideoView")!;
    videoView.MediaPlayer = _player;
    _player.Play(media);
  }

  private void DetachFeed(VideoFeed? feed)
  {
    if (feed == null) return;

    if (_mediaInput != null)
    {
      feed.OnInit -= _mediaInput.OnInit;
      feed.OnGop -= _mediaInput.OnGop;
    }

    _player?.Stop();
    _player?.Dispose();
    _player = null;
    _mediaInput?.Dispose();
    _mediaInput = null;
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

    if (data.Length < 2 || Bounds.Width == 0 || Bounds.Height == 0)
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
          Fill = MotionCellBrush,
          IsHitTestVisible = false
        };
        Canvas.SetLeft(rect, col * cellWidth);
        Canvas.SetTop(rect, row * cellHeight);
        _motionOverlay.Children.Add(rect);
      }
    }
  }

  private sealed class FeedMediaInput : MediaInput
  {
    private readonly Lock _lock = new();
    private readonly ManualResetEventSlim _dataAvailable = new(false);
    private readonly MemoryStream _buffer = new();
    private ReadOnlyMemory<byte> _initSegment;
    private long _readPosition;
    private bool _closed;

    protected override void Dispose(bool disposing)
    {
      if (disposing)
      {
        _closed = true;
        _dataAvailable.Set();
        _buffer.Dispose();
        _dataAvailable.Dispose();
      }
      base.Dispose(disposing);
    }

    public void OnInit(ReadOnlyMemory<byte> data)
    {
      lock (_lock)
      {
        _initSegment = data.ToArray();
        _buffer.SetLength(0);
        _buffer.Write(_initSegment.Span);
        _readPosition = 0;
      }
      _dataAvailable.Set();
    }

    public void OnGop(GopMessage gop)
    {
      lock (_lock)
      {
        _buffer.Write(gop.Data.Span);
      }
      _dataAvailable.Set();
    }

    public override bool Open(out ulong size)
    {
      size = ulong.MaxValue;
      return true;
    }

    public override void Close()
    {
      _closed = true;
      _dataAvailable.Set();
    }

    public override int Read(nint buf, uint len)
    {
      while (!_closed)
      {
        lock (_lock)
        {
          var available = _buffer.Length - _readPosition;
          if (available > 0)
          {
            var toRead = (int)Math.Min(len, available);
            System.Runtime.InteropServices.Marshal.Copy(
              _buffer.GetBuffer(), (int)_readPosition, buf, toRead);
            _readPosition += toRead;

            if (_readPosition > 256 * 1024)
            {
              var remaining = (int)(_buffer.Length - _readPosition);
              if (remaining > 0)
              {
                var tail = _buffer.GetBuffer().AsSpan((int)_readPosition, remaining);
                _buffer.Position = 0;
                _buffer.Write(tail);
                _buffer.SetLength(remaining);
              }
              else
              {
                _buffer.SetLength(0);
              }
              _readPosition = 0;
            }

            return toRead;
          }
        }

        _dataAvailable.Reset();
        _dataAvailable.Wait();
      }

      return 0;
    }

    public override bool Seek(ulong offset)
    {
      lock (_lock)
      {
        if ((long)offset <= _buffer.Length)
        {
          _readPosition = (long)offset;
          return true;
        }
        return false;
      }
    }
  }
}
