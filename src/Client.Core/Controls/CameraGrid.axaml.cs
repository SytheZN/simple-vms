using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Diagnostics.CodeAnalysis;

namespace Client.Core.Controls;

[ExcludeFromCodeCoverage]
public partial class CameraGrid : UserControl
{
  public static readonly StyledProperty<int> ColumnsProperty =
    AvaloniaProperty.Register<CameraGrid, int>(nameof(Columns), 3);

  private const double CondensedThreshold = 200;

  private ItemsControl? _gridItems;
  private UniformGrid? _gridPanel;
  private bool _isCondensed;

  public int Columns
  {
    get => GetValue(ColumnsProperty);
    set => SetValue(ColumnsProperty, value);
  }

  public event Action<int>? ItemClicked;

  public CameraGrid()
  {
    InitializeComponent();
    _gridItems = this.FindControl<ItemsControl>("GridItems");
    AddHandler(PointerReleasedEvent, OnPointerReleased, handledEventsToo: true);
    SizeChanged += OnSizeChanged;
  }

  protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
  {
    base.OnLoaded(e);
    SyncGridPanel();
  }

  protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
  {
    base.OnPropertyChanged(change);

    if (change.Property == ColumnsProperty)
    {
      SyncGridPanel();
      UpdateCondensedState();
    }
  }

  private void SyncGridPanel()
  {
    _gridPanel ??= _gridItems?.GetVisualDescendants().OfType<UniformGrid>().FirstOrDefault();
    if (_gridPanel != null)
      _gridPanel.Columns = Columns;
  }

  private void OnSizeChanged(object? sender, SizeChangedEventArgs e) =>
    UpdateCondensedState();

  private void UpdateCondensedState()
  {
    var columns = _gridPanel?.Columns ?? Columns;
    if (columns <= 0 || Bounds.Width <= 0) return;
    var cardWidth = Bounds.Width / columns;
    var condensed = cardWidth < CondensedThreshold;
    if (condensed == _isCondensed) return;
    _isCondensed = condensed;
    PseudoClasses.Set(":condensed", _isCondensed);
  }

  public void FlashCamera(int index)
  {
    if (_gridPanel == null || index < 0 || index >= _gridPanel.Children.Count) return;

    var container = _gridPanel.Children[index];
    var card = container.GetVisualDescendants()
      .OfType<Border>()
      .FirstOrDefault(b => b.Classes.Contains("card"));
    if (card == null) return;

    IBrush? flashBrush = null;
    if (Application.Current?.TryGetResource("WarningBrush",
          Application.Current.ActualThemeVariant, out var res) == true)
      flashBrush = res as IBrush;
    flashBrush ??= Brushes.Orange;

    card.BorderBrush = flashBrush;
    card.BorderThickness = new Thickness(2);

    DispatcherTimer.RunOnce(() =>
    {
      card.ClearValue(Border.BorderBrushProperty);
      card.ClearValue(Border.BorderThicknessProperty);
    }, TimeSpan.FromMilliseconds(800));
  }

  private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
  {
    if (_gridPanel == null || _gridItems == null) return;

    var pos = e.GetPosition(_gridItems);
    for (var i = 0; i < _gridPanel.Children.Count; i++)
    {
      if (_gridPanel.Children[i] is not Control control) continue;
      var topLeft = control.TranslatePoint(new Point(0, 0), _gridItems);
      if (topLeft == null) continue;
      if (new Rect(topLeft.Value, control.Bounds.Size).Contains(pos))
      {
        ItemClicked?.Invoke(i);
        return;
      }
    }
  }
}
