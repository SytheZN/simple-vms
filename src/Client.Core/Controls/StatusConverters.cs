using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using System.Diagnostics.CodeAnalysis;

namespace Client.Core.Controls;

[ExcludeFromCodeCoverage]
public sealed class StatusToIconKindConverter : IValueConverter
{
  public static readonly StatusToIconKindConverter Instance = new();

  public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
    value?.ToString() switch
    {
      "online" or "recording" => PhosphorIconKind.VideoCamera,
      "error" => PhosphorIconKind.Warning,
      _ => PhosphorIconKind.VideoCameraSlash
    };

  public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
    throw new NotSupportedException();
}

[ExcludeFromCodeCoverage]
public sealed class StatusToBadgeBackgroundConverter : IValueConverter
{
  public static readonly StatusToBadgeBackgroundConverter Instance = new();

  public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    var key = value?.ToString() switch
    {
      "recording" => "DangerMutedBrush",
      "error" => "WarningMutedBrush",
      "online" => "PrimaryMutedBrush",
      _ => "SurfaceSunkenBrush"
    };
    if (Application.Current?.TryGetResource(key, Application.Current.ActualThemeVariant, out var res) == true)
      return res;
    return null;
  }

  public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
    throw new NotSupportedException();
}

[ExcludeFromCodeCoverage]
public sealed class StatusToBadgeForegroundConverter : IValueConverter
{
  public static readonly StatusToBadgeForegroundConverter Instance = new();

  public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    var key = value?.ToString() switch
    {
      "recording" => "DangerBrush",
      "error" => "WarningBrush",
      "online" => "PrimaryBrush",
      _ => "TextMutedBrush"
    };
    if (Application.Current?.TryGetResource(key, Application.Current.ActualThemeVariant, out var res) == true)
      return res;
    return null;
  }

  public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
    throw new NotSupportedException();
}
