namespace Client.Core.Controls;

public sealed class TimelineViewport
{
  public const double MinVisibleUs = 3 * 60 * 1_000_000d;
  public const double MaxVisibleUs = 30 * 86_400 * 1_000_000d;

  private double _playheadUs;
  private double _visibleUs = 4 * 3600 * 1_000_000d;
  private double _width = 1;
  private double _playheadFraction = 0.8;

  public double Width
  {
    get => _width;
    set => _width = Math.Max(1, value);
  }

  public double PlayheadFraction
  {
    get => _playheadFraction;
    set => _playheadFraction = Math.Clamp(value, 0, 1);
  }

  public ulong PlayheadTimestamp
  {
    get => (ulong)Math.Max(0, _playheadUs);
    set => _playheadUs = value;
  }

  public double VisibleDurationUs
  {
    get => _visibleUs;
    set => _visibleUs = Math.Clamp(value, MinVisibleUs, MaxVisibleUs);
  }

  public double UsPerPixel => _visibleUs / _width;

  public double PlayheadX => _playheadFraction * _width;

  public double FromUs => _playheadUs - _playheadFraction * _visibleUs;

  public double ToUs => FromUs + _visibleUs;

  public ulong VisibleFrom => (ulong)Math.Max(0, FromUs);

  public ulong VisibleTo => (ulong)Math.Max(0, ToUs);

  public double XOf(double timestampUs) => (timestampUs - FromUs) / _visibleUs * _width;

  public double TimeAt(double x) => FromUs + x / _width * _visibleUs;

  public void PanByPixels(double dx) => _playheadUs = Math.Max(0, _playheadUs - dx * UsPerPixel);

  public void ZoomTo(double visibleUs)
  {
    var anchorUs = _playheadUs;
    VisibleDurationUs = visibleUs;
    _playheadUs = anchorUs;
  }
}
