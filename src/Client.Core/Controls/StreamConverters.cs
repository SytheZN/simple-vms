using System.Globalization;
using Avalonia.Data.Converters;
using Shared.Api;
using Shared.Models;
using System.Diagnostics.CodeAnalysis;

namespace Client.Core.Controls;

[ExcludeFromCodeCoverage]
public sealed class QualityStreamsConverter : IValueConverter
{
  public static readonly QualityStreamsConverter Instance = new();

  public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
    value is IEnumerable<StreamProfileDto> streams
      ? streams.Where(s => s.Kind == StreamKind.Quality).ToList()
      : Array.Empty<StreamProfileDto>();

  public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
    throw new NotSupportedException();
}
