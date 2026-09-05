using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Shared.Models;
using Shared.Models.Formats;

namespace Analyzer.MotionGridH26x;

internal sealed class MotionGridH264Worker : IDataStream<MotionGridUnit>, IAsyncDisposable
{
  private bool _disposed;
  private readonly Guid _cameraId;
  private readonly string _parentProfile;
  private readonly IDataStream<H264NalUnit> _input;
  private readonly Channel<MotionGridUnit> _output;
  private readonly H264SkipExtractor _extractor;
  private readonly MotionGridProcessor _processor;
  private readonly CancellationTokenSource _cts;
  private readonly Task _runLoop;
  private readonly ILogger _logger;

  public StreamInfo Info { get; }
  public Type FrameType => typeof(MotionGridUnit);

  public MotionGridH264Worker(
    Guid cameraId, string parentProfile,
    IDataStream<H264NalUnit> input, MotionGridProcessor processor, ILogger logger)
  {
    _cameraId = cameraId;
    _parentProfile = parentProfile;
    _input = input;
    _processor = processor;
    _logger = logger;
    _extractor = new H264SkipExtractor(logger);
    _output = Channel.CreateBounded<MotionGridUnit>(new BoundedChannelOptions(64)
    {
      FullMode = BoundedChannelFullMode.DropOldest,
      SingleReader = true,
      SingleWriter = true
    });
    Info = new StreamInfo { DataFormat = "motion-grid", Fps = input.Info.Fps };
    _cts = new CancellationTokenSource();
    _runLoop = Task.Run(() => RunAsync(_cts.Token));
  }

  private async Task RunAsync(CancellationToken ct)
  {
    try
    {
      await foreach (var nal in _input.ReadAsync(ct))
      {
        if (_extractor.TryFeed(nal, out var emitted) && emitted != null)
        {
          _processor.Feed(emitted);
          while (_processor.TryReceive(out var unit))
            await _output.Writer.WriteAsync(unit, ct);
        }
      }
      var flushed = _extractor.Flush();
      if (flushed != null)
        _processor.Feed(flushed);
      _processor.Flush();
      while (_processor.TryReceive(out var unit))
        await _output.Writer.WriteAsync(unit, ct);
    }
    catch (OperationCanceledException) { }
    catch (Exception ex)
    {
      _logger.LogError(ex, "MotionGridH264Worker failed for camera {CameraId} profile '{Profile}'",
        _cameraId, _parentProfile);
    }
    finally
    {
      (_input as IDisposable)?.Dispose();
      _output.Writer.TryComplete();
    }
  }

  public async IAsyncEnumerable<MotionGridUnit> ReadAsync(
    [EnumeratorCancellation] CancellationToken ct)
  {
    while (true)
    {
      bool available;
      try { available = await _output.Reader.WaitToReadAsync(ct); }
      catch (OperationCanceledException) { yield break; }
      if (!available) break;
      while (_output.Reader.TryRead(out var unit))
        yield return unit;
    }
  }

  IAsyncEnumerable<IDataUnit> IDataStream.ReadAsync(CancellationToken ct) =>
    ReadAsDataUnits(ct);

  private async IAsyncEnumerable<IDataUnit> ReadAsDataUnits(
    [EnumeratorCancellation] CancellationToken ct)
  {
    await foreach (var unit in ReadAsync(ct))
      yield return unit;
  }

  public async ValueTask DisposeAsync()
  {
    if (_disposed) return;
    _disposed = true;
    _cts.Cancel();
    await _runLoop.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    _cts.Dispose();
  }
}
