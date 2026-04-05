using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using System.Diagnostics.CodeAnalysis;

namespace Client.Core.Controls;

[ExcludeFromCodeCoverage]
public partial class CameraGrid : UserControl
{
  public static readonly StyledProperty<int> ColumnsProperty =
    AvaloniaProperty.Register<CameraGrid, int>(nameof(Columns), 3);

  private ItemsControl? _gridItems;
  private UniformGrid? _gridPanel;
  private Control? _dragItem;
  private Point _dragStart;
  private int _dragSourceIndex;
  private bool _isDragging;

  public int Columns
  {
    get => GetValue(ColumnsProperty);
    set => SetValue(ColumnsProperty, value);
  }

  public event Action<int, int>? ItemReordered;

  public CameraGrid()
  {
    InitializeComponent();
    _gridItems = this.FindControl<ItemsControl>("GridItems");
    _gridPanel = _gridItems?.GetVisualDescendants().OfType<UniformGrid>().FirstOrDefault();
    AddHandler(PointerPressedEvent, OnGridPointerPressed, handledEventsToo: true);
    AddHandler(PointerMovedEvent, OnGridPointerMoved, handledEventsToo: true);
    AddHandler(PointerReleasedEvent, OnGridPointerReleased, handledEventsToo: true);
  }

  protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
  {
    base.OnPropertyChanged(change);

    if (change.Property == ColumnsProperty)
    {
      if (_gridPanel != null)
        _gridPanel.Columns = change.GetNewValue<int>();
    }
  }

  private void OnGridPointerPressed(object? sender, PointerPressedEventArgs e)
  {
    if (_gridItems == null || _gridPanel == null) return;

    var pos = e.GetPosition(_gridItems);
    _dragStart = pos;
    _dragItem = FindItemAtPoint(_gridPanel, _gridItems, pos);

    if (_dragItem != null)
    {
      _dragSourceIndex = IndexOfContainer(_gridPanel, _dragItem);
    }
  }

  private void OnGridPointerMoved(object? sender, PointerEventArgs e)
  {
    if (_dragItem == null) return;

    var pos = e.GetPosition(this);
    var delta = pos - _dragStart;
    if (!_isDragging && (Math.Abs(delta.X) > 5 || Math.Abs(delta.Y) > 5))
      _isDragging = true;

    if (_isDragging)
      _dragItem.Opacity = 0.5;
  }

  private void OnGridPointerReleased(object? sender, PointerReleasedEventArgs e)
  {
    if (_dragItem != null && _isDragging)
    {
      _dragItem.Opacity = 1.0;

      if (_gridItems != null && _gridPanel != null)
      {
        var pos = e.GetPosition(_gridItems);
        var targetItem = FindItemAtPoint(_gridPanel, _gridItems, pos);
        if (targetItem != null && targetItem != _dragItem)
        {
          var targetIndex = IndexOfContainer(_gridPanel, targetItem);
          if (_dragSourceIndex >= 0 && targetIndex >= 0)
            ItemReordered?.Invoke(_dragSourceIndex, targetIndex);
        }
      }
    }

    _dragItem = null;
    _isDragging = false;
  }

  private static Control? FindItemAtPoint(UniformGrid panel, ItemsControl items, Point point)
  {
    foreach (var child in panel.Children)
    {
      if (child is not Control control) continue;

      var topLeft = control.TranslatePoint(new Point(0, 0), items);
      if (topLeft == null) continue;

      var rect = new Rect(topLeft.Value, control.Bounds.Size);
      if (rect.Contains(point))
        return control;
    }
    return null;
  }

  private static int IndexOfContainer(UniformGrid panel, Control container)
  {
    for (var i = 0; i < panel.Children.Count; i++)
    {
      if (panel.Children[i] == container)
        return i;
    }
    return -1;
  }
}
