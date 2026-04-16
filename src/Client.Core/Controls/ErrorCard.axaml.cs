using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Shared.Models;
using System.Diagnostics.CodeAnalysis;

namespace Client.Core.Controls;

[ExcludeFromCodeCoverage]
public partial class ErrorCard : UserControl
{
  public static readonly StyledProperty<string?> MessageProperty =
    AvaloniaProperty.Register<ErrorCard, string?>(nameof(Message));

  public static readonly StyledProperty<DebugTag?> DebugTagProperty =
    AvaloniaProperty.Register<ErrorCard, DebugTag?>(nameof(Tag));

  public static readonly StyledProperty<string?> CopyDataProperty =
    AvaloniaProperty.Register<ErrorCard, string?>(nameof(CopyData));

  private readonly Border _root;
  private readonly TextBlock _messageLabel;
  private readonly TextBlock _tagLabel;
  private readonly StackPanel _tagRow;

  public string? Message
  {
    get => GetValue(MessageProperty);
    set => SetValue(MessageProperty, value);
  }

  public DebugTag? DebugTag
  {
    get => GetValue(DebugTagProperty);
    set => SetValue(DebugTagProperty, value);
  }

  public string? CopyData
  {
    get => GetValue(CopyDataProperty);
    set => SetValue(CopyDataProperty, value);
  }

  public ErrorCard()
  {
    InitializeComponent();
    _root = this.FindControl<Border>("Root")!;
    _messageLabel = this.FindControl<TextBlock>("MessageLabel")!;
    _tagLabel = this.FindControl<TextBlock>("TagLabel")!;
    _tagRow = this.FindControl<StackPanel>("TagRow")!;
    this.FindControl<Button>("CopyButton")!.Click += OnCopyClick;
  }

  protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
  {
    base.OnPropertyChanged(change);

    if (change.Property == MessageProperty)
    {
      var msg = change.GetNewValue<string?>();
      _root.IsVisible = !string.IsNullOrEmpty(msg);
      _messageLabel.Text = msg;
    }
    else if (change.Property == DebugTagProperty)
    {
      var tag = change.GetNewValue<DebugTag?>();
      _tagLabel.Text = tag?.ToString();
      _tagRow.IsVisible = tag != null;
    }
  }

  private async void OnCopyClick(object? sender, RoutedEventArgs e)
  {
    var data = CopyData;
    if (string.IsNullOrEmpty(data)) return;

    var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
    if (clipboard != null)
      await clipboard.SetValueAsync(DataFormat.Text, data);
  }
}
