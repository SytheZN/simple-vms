using System.Globalization;
using Avalonia.Data.Converters;
using System.Diagnostics.CodeAnalysis;

namespace Client.Core.Controls;

[ExcludeFromCodeCoverage]
public sealed class TimestampConverter : IValueConverter
{
  public static readonly TimestampConverter Instance = new();

  public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    if (value is ulong us && us > 0)
      return DateTimeOffset.FromUnixTimeMilliseconds((long)(us / 1000))
        .LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
    return "--";
  }

  public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
    throw new NotSupportedException();
}

[ExcludeFromCodeCoverage]
public sealed class NullableTimestampConverter : IValueConverter
{
  public static readonly NullableTimestampConverter Instance = new();

  public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    if (value is ulong us && us > 0)
      return DateTimeOffset.FromUnixTimeMilliseconds((long)(us / 1000))
        .LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
    return "--";
  }

  public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
    throw new NotSupportedException();
}
