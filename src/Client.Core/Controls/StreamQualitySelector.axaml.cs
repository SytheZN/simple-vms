using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Shared.Models.Dto;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Client.Core.Controls;

[ExcludeFromCodeCoverage]
public partial class StreamQualitySelector : UserControl
{
  public static readonly StyledProperty<IReadOnlyList<StreamProfileDto>?> StreamsProperty =
    AvaloniaProperty.Register<StreamQualitySelector, IReadOnlyList<StreamProfileDto>?>(nameof(Streams));

  public static readonly StyledProperty<string> SelectedProfileProperty =
    AvaloniaProperty.Register<StreamQualitySelector, string>(nameof(SelectedProfile), "main");

  private readonly TextBlock _label;

  public IReadOnlyList<StreamProfileDto>? Streams
  {
    get => GetValue(StreamsProperty);
    set => SetValue(StreamsProperty, value);
  }

  public string SelectedProfile
  {
    get => GetValue(SelectedProfileProperty);
    set => SetValue(SelectedProfileProperty, value);
  }

  public event Action<string>? ProfileChanged;

  public StreamQualitySelector()
  {
    InitializeComponent();
    _label = this.FindControl<TextBlock>("ProfileLabel")!;
    var button = this.FindControl<Button>("CycleButton")!;
    button.Click += OnCycleClick;
    UpdateLabel();
  }

  protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
  {
    base.OnPropertyChanged(change);
    if (change.Property == StreamsProperty || change.Property == SelectedProfileProperty)
      UpdateLabel();
  }

  private void OnCycleClick(object? sender, RoutedEventArgs e)
  {
    var streams = Streams;
    if (streams == null || streams.Count <= 1) return;

    var profiles = streams.Select(s => s.Profile).ToList();
    var idx = profiles.IndexOf(SelectedProfile);
    var next = profiles[(idx + 1) % profiles.Count];
    SelectedProfile = next;
    ProfileChanged?.Invoke(next);
    UpdateLabel();
  }

  private void UpdateLabel()
  {
    if (_label == null) return;

    var streams = Streams;
    if (streams == null || streams.Count == 0)
    {
      _label.Text = "n/a";
      return;
    }
    var current = streams.FirstOrDefault(s => s.Profile == SelectedProfile);
    _label.Text = current != null
      ? $"{SelectedProfile} ({current.Resolution} {current.Codec})"
      : SelectedProfile;
  }
}
