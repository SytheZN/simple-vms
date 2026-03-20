namespace Format.Fmp4;

public sealed class TimestampConverter
{
  private readonly uint _timescale;
  private ulong _baseTimestamp;
  private bool _initialized;

  public TimestampConverter(uint timescale = 90000)
  {
    _timescale = timescale;
  }

  public uint Timescale => _timescale;

  public ulong ToDecodeTime(ulong unixMicros)
  {
    if (!_initialized)
    {
      _baseTimestamp = unixMicros;
      _initialized = true;
    }

    var deltaMicros = unixMicros - _baseTimestamp;
    return deltaMicros * _timescale / 1_000_000;
  }

  public uint DurationBetween(ulong fromMicros, ulong toMicros)
  {
    var deltaMicros = toMicros - fromMicros;
    return (uint)(deltaMicros * _timescale / 1_000_000);
  }

  public void Reset()
  {
    _initialized = false;
    _baseTimestamp = 0;
  }
}
