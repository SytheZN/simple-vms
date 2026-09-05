using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Shared.Models;
using Shared.Models.Formats;

namespace Analyzer.Thumbnail;

internal sealed class ThumbnailWorker : IDataStream<JpegUnit>, IAsyncDisposable
{
  private readonly Guid _cameraId;
  private readonly string _parentProfile;
  private readonly IDataStream _input;
  private readonly bool _isH265;
  private readonly Func<int> _boundingSize;
  private readonly Func<int> _quality;
  private readonly Func<ulong> _intervalMicros;
  private readonly ILogger _logger;
  private readonly ILogger _perfLogger;

  private readonly H264KeyframeDecoder _h264;
  private readonly H265KeyframeDecoder _h265;
  private readonly ThumbnailEncoder _encoder = new();

  private ulong _lastEmitted;

  public StreamInfo Info { get; }
  public Type FrameType => typeof(JpegUnit);

  public ThumbnailWorker(
    Guid cameraId, string parentProfile, IDataStream input, bool isH265,
    Func<int> boundingSize, Func<int> quality, Func<ulong> intervalMicros,
    ILogger logger, ILogger perfLogger)
  {
    _cameraId = cameraId;
    _parentProfile = parentProfile;
    _input = input;
    _isH265 = isH265;
    _boundingSize = boundingSize;
    _quality = quality;
    _intervalMicros = intervalMicros;
    _logger = logger;
    _perfLogger = perfLogger;
    _h264 = new H264KeyframeDecoder(logger);
    _h265 = new H265KeyframeDecoder(logger);
    Info = new StreamInfo { DataFormat = "mjpeg", Fps = input.Info.Fps };
    SeedParameterSets(input.Info.FormatParameters);
  }

  private void SeedParameterSets(object? formatParameters)
  {
    switch (formatParameters)
    {
      case H265Parameters h265 when _isH265:
        _h265.AddParameterSet(h265.Sps.Span, 33);
        _h265.AddParameterSet(h265.Pps.Span, 34);
        _logger.LogDebug("Seeded SDP parameter sets: sps {SpsBytes} bytes, pps {PpsBytes} bytes",
          h265.Sps.Length, h265.Pps.Length);
        break;
      case H264Parameters h264 when !_isH265:
        _h264.AddParameterSet(h264.Sps.Span, 7);
        _h264.AddParameterSet(h264.Pps.Span, 8);
        _logger.LogDebug("Seeded SDP parameter sets: sps {SpsBytes} bytes, pps {PpsBytes} bytes",
          h264.Sps.Length, h264.Pps.Length);
        break;
      default:
        _logger.LogWarning(
          "Camera {CameraId} profile '{Profile}' carries no SDP parameter sets; waiting for in-band SPS/PPS",
          _cameraId, _parentProfile);
        break;
    }
  }

  public async IAsyncEnumerable<JpegUnit> ReadAsync(
    [EnumeratorCancellation] CancellationToken ct)
  {
    _logger.LogDebug("Reading camera {CameraId} profile '{Profile}' as {Codec}",
      _cameraId, _parentProfile, _isH265 ? "h265" : "h264");

    var units = 0;
    var keyframes = 0;

    try
    {
      await foreach (var unit in _input.ReadAsync(ct))
      {
        if (unit.Data.Length < 2) continue;
        units++;
        if (unit.IsSyncPoint) keyframes++;

        var thumbnail = _isH265 ? Handle265(unit) : Handle264(unit);
        if (thumbnail == null) continue;

        _logger.LogTrace("Emitting {Width}x{Height} thumbnail, {Bytes} bytes (unit {Units}, keyframe {Keyframes})",
          thumbnail.Width, thumbnail.Height, thumbnail.Data.Length, units, keyframes);

        _lastEmitted = unit.Timestamp;
        yield return thumbnail;
      }
    }
    finally
    {
      (_input as IDisposable)?.Dispose();

      _logger.LogDebug("Read ended for camera {CameraId} profile '{Profile}' after {Units} units, {Keyframes} keyframes",
        _cameraId, _parentProfile, units, keyframes);
    }
  }

  private JpegUnit? Handle264(IDataUnit unit)
  {
    var header = unit.Data.Span[0];
    var nalUnitType = (byte)(header & 0x1F);

    if (nalUnitType is 7 or 8)
    {
      _h264.AddParameterSet(unit.Data.Span, nalUnitType);
      return null;
    }

    if (!unit.IsSyncPoint || !IsDue(unit.Timestamp)) return null;

    return Render(() => _h264.Decode(
      unit.Data.Span, nalUnitType, (byte)((header >> 5) & 3)), unit.Timestamp);
  }

  private JpegUnit? Handle265(IDataUnit unit)
  {
    var nalUnitType = (byte)((unit.Data.Span[0] >> 1) & 0x3F);

    if (nalUnitType is 32 or 33 or 34)
    {
      _h265.AddParameterSet(unit.Data.Span, nalUnitType);
      return null;
    }

    if (!unit.IsSyncPoint || !IsDue(unit.Timestamp)) return null;

    return Render(() => _h265.Decode(unit.Data.Span, nalUnitType, _boundingSize()), unit.Timestamp);
  }

  private delegate DecodedFrame? DecodeAttempt();

  private JpegUnit? Render(DecodeAttempt attempt, ulong timestamp)
  {
    var started = Stopwatch.GetTimestamp();

    DecodedFrame? frame;
    try
    {
      frame = attempt();
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Keyframe decode threw for camera {CameraId} profile '{Profile}'",
        _cameraId, _parentProfile);
      return null;
    }

    if (frame == null) return null;

    var decoded = Stopwatch.GetTimestamp();
    var encoded = _encoder.Encode(frame, _boundingSize(), _quality());
    var finished = Stopwatch.GetTimestamp();

    _perfLogger.LogTrace(
      "Thumbnail for camera {CameraId} profile '{Profile}': {Source} to {Width}x{Height}, " +
      "{Bytes} bytes, decode {DecodeMs:F1}ms encode {EncodeMs:F1}ms",
      _cameraId, _parentProfile, $"{frame.LumaWidth}x{frame.LumaHeight}",
      encoded.Width, encoded.Height, encoded.Data.Length,
      Stopwatch.GetElapsedTime(started, decoded).TotalMilliseconds,
      Stopwatch.GetElapsedTime(decoded, finished).TotalMilliseconds);

    return new JpegUnit
    {
      Data = encoded.Data,
      Timestamp = timestamp,
      Width = encoded.Width,
      Height = encoded.Height
    };
  }

  private bool IsDue(ulong timestamp)
  {
    var interval = _intervalMicros();
    return interval == 0 || _lastEmitted == 0 || timestamp - _lastEmitted >= interval;
  }

  IAsyncEnumerable<IDataUnit> IDataStream.ReadAsync(CancellationToken ct) =>
    ReadAsDataUnits(ct);

  private async IAsyncEnumerable<IDataUnit> ReadAsDataUnits(
    [EnumeratorCancellation] CancellationToken ct)
  {
    await foreach (var unit in ReadAsync(ct))
      yield return unit;
  }

  public ValueTask DisposeAsync()
  {
    (_input as IDisposable)?.Dispose();
    return ValueTask.CompletedTask;
  }
}
