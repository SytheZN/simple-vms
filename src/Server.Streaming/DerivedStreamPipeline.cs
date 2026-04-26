using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Server.Plugins;
using Shared.Models;

namespace Server.Streaming;

public sealed class DerivedStreamPipeline : IPipeline
{
  private readonly Guid _cameraId;
  private readonly string _profile;
  private readonly string _parentProfile;
  private readonly IDataStreamAnalyzer _analyzerIdentity;
  private readonly IDataStreamAnalyzerStreamOutput _analyzer;
  private readonly IStreamFormat _format;
  private readonly ILogger _logger;
  private readonly Lock _lock = new();

  private IDataStreamFanOut? _dataFanOut;
  private IMuxStreamFanOut? _muxFanOut;
  private IDisposable? _muxSubscription;
  private CancellationTokenSource? _runCts;
  private Task? _feedLoop;
  private Type? _frameType;
  private bool _constructed;
  private bool _disposed;

  public Guid CameraId => _cameraId;
  public string Profile => _profile;
  public string ProducerId => _analyzerIdentity.AnalyzerId;
  public string FormatId => _format.FormatId;
  public bool IsConstructed { get { lock (_lock) return _constructed; } }
  public ReadOnlyMemory<byte> MuxHeader { get { lock (_lock) return _muxFanOut?.Header ?? ReadOnlyMemory<byte>.Empty; } }

  public DerivedStreamPipeline(
    Guid cameraId,
    string profile,
    string parentProfile,
    IDataStreamAnalyzer analyzerIdentity,
    IDataStreamAnalyzerStreamOutput analyzer,
    IStreamFormat format,
    ILogger logger)
  {
    _cameraId = cameraId;
    _profile = profile;
    _parentProfile = parentProfile;
    _analyzerIdentity = analyzerIdentity;
    _analyzer = analyzer;
    _format = format;
    _logger = logger;
  }

  [RequiresDynamicCode("Pipeline construction uses dynamic fan-out types")]
  public async Task<OneOf<Success, Error>> ConstructAsync(CancellationToken ct)
  {
    lock (_lock)
      if (_constructed) return new Success();

    using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    var startResult = await _analyzer.StartStreamAsync(_cameraId, _parentProfile, probeCts.Token);
    if (startResult.IsT1)
    {
      _logger.LogError("Analyzer {AnalyzerId} StartAsync failed for camera {CameraId} profile '{ParentProfile}': {Message}",
        _analyzerIdentity.AnalyzerId, _cameraId, _parentProfile, startResult.AsT1.Message);
      return startResult.AsT1;
    }

    var dataStream = startResult.AsT0;
    var fanOut = CreateDataFanOut(dataStream);
    var muxInput = fanOut.SubscribePassive(256);
    var muxSub = muxInput as IDisposable;

    IMuxStreamFanOut? muxFanOut = null;
    var pipelineResult = await _format.CreatePipelineAsync(muxInput, dataStream.Info, ct);
    if (pipelineResult.IsT0)
    {
      muxFanOut = CreateMuxFanOut(pipelineResult.AsT0);
      _logger.LogInformation(
        "Derived pipeline constructed for camera {CameraId} profile '{Profile}', analyzer {AnalyzerId}",
        _cameraId, _profile, _analyzerIdentity.AnalyzerId);
    }
    else
    {
      _logger.LogWarning("Format pipeline failed for derived stream {CameraId}/{Profile}: {Message}",
        _cameraId, _profile, pipelineResult.AsT1.Message);
    }

    probeCts.Cancel();

    lock (_lock)
    {
      _dataFanOut = fanOut;
      _muxFanOut = muxFanOut;
      _muxSubscription = muxSub;
      _frameType = dataStream.FrameType;
      _constructed = true;
    }

    return new Success();
  }

  public Task<OneOf<IDataStream, Error>> SubscribeDataAsync(CancellationToken ct)
  {
    lock (_lock)
    {
      if (_disposed)
        return Task.FromResult<OneOf<IDataStream, Error>>(Error.Create(
          ModuleIds.Streaming, 0x0010, Result.Unavailable, "Pipeline has been disposed"));
      if (!_constructed)
        return Task.FromResult<OneOf<IDataStream, Error>>(Error.Create(
          ModuleIds.Streaming, 0x0011, Result.Unavailable, "Pipeline not constructed"));
      return Task.FromResult(OneOf<IDataStream, Error>.FromT0(_dataFanOut!.Subscribe(256)));
    }
  }

  public Task<OneOf<IMuxStream, Error>> SubscribeMuxAsync(CancellationToken ct)
  {
    lock (_lock)
    {
      if (_disposed)
        return Task.FromResult<OneOf<IMuxStream, Error>>(Error.Create(
          ModuleIds.Streaming, 0x0012, Result.Unavailable, "Pipeline has been disposed"));
      if (!_constructed)
        return Task.FromResult<OneOf<IMuxStream, Error>>(Error.Create(
          ModuleIds.Streaming, 0x0013, Result.Unavailable, "Pipeline not constructed"));
      if (_muxFanOut == null)
        return Task.FromResult<OneOf<IMuxStream, Error>>(Error.Create(
          ModuleIds.Streaming, 0x0014, Result.Unavailable, "No mux pipeline available"));

      return Task.FromResult(OneOf<IMuxStream, Error>.FromT0(_muxFanOut.Subscribe(256)));
    }
  }

  private void OnDemand()
  {
    _ = Task.Run(async () =>
    {
      _logger.LogDebug("Derived demand for camera {CameraId} profile '{Profile}'", _cameraId, _profile);
      await StartRunAsync(CancellationToken.None);
    });
  }

  private void OnEmpty()
  {
    var dataSubs = _dataFanOut?.SubscriberCount ?? 0;
    var muxSubs = _muxFanOut?.SubscriberCount ?? 0;
    if (dataSubs + muxSubs > 0) return;

    _ = Task.Run(async () =>
    {
      _logger.LogDebug("Derived idle for camera {CameraId} profile '{Profile}'", _cameraId, _profile);
      await StopRunAsync();
    });
  }

  private async Task StartRunAsync(CancellationToken ct)
  {
    CancellationTokenSource cts;
    lock (_lock)
    {
      if (_disposed) return;
      if (_runCts != null) return;
      cts = _runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    }

    var startResult = await _analyzer.StartStreamAsync(_cameraId, _parentProfile, cts.Token);
    if (startResult.IsT1)
    {
      _logger.LogError("Analyzer {AnalyzerId} StartAsync failed: {Message}",
        _analyzerIdentity.AnalyzerId, startResult.AsT1.Message);
      lock (_lock) _runCts = null;
      cts.Dispose();
      return;
    }

    var stream = startResult.AsT0;
    if (stream.FrameType != _frameType)
    {
      _logger.LogWarning("Analyzer {AnalyzerId} returned stream of {Actual}, expected {Expected}",
        _analyzerIdentity.AnalyzerId, stream.FrameType.Name, _frameType?.Name);
      lock (_lock) _runCts = null;
      cts.Dispose();
      return;
    }

    var feed = Task.Run(async () =>
    {
      try
      {
        await foreach (var item in stream.ReadAsync(cts.Token))
          _dataFanOut!.Write(item);
      }
      catch (OperationCanceledException) { }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Derived feed loop failed for {CameraId}/{Profile}", _cameraId, _profile);
      }
    });

    lock (_lock) _feedLoop = feed;
  }

  private async Task StopRunAsync()
  {
    CancellationTokenSource? cts;
    Task? loop;
    lock (_lock)
    {
      cts = _runCts;
      _runCts = null;
      loop = _feedLoop;
      _feedLoop = null;
    }

    if (cts != null)
    {
      cts.Cancel();
      if (loop != null)
      {
        try { await loop; }
        catch { }
      }
      cts.Dispose();
    }
  }

  [RequiresDynamicCode("Fan-out generic type is constructed at runtime")]
  private IDataStreamFanOut CreateDataFanOut(IDataStream dataStream)
  {
    var fanOutType = typeof(DataStreamFanOut<>).MakeGenericType(dataStream.FrameType);
    var fanOut = (IDataStreamFanOut)Activator.CreateInstance(fanOutType, dataStream.Info)!;
    fanOut.OnDemand = OnDemand;
    fanOut.OnEmpty = OnEmpty;
    fanOut.Logger = _logger;
    return fanOut;
  }

  [RequiresDynamicCode("Fan-out generic type is constructed at runtime")]
  private IMuxStreamFanOut CreateMuxFanOut(IMuxStream muxStream)
  {
    var fanOutType = typeof(MuxStreamFanOut<>).MakeGenericType(muxStream.FrameType);
    var fanOut = (IMuxStreamFanOut)Activator.CreateInstance(fanOutType, muxStream)!;
    fanOut.OnDemand = OnDemand;
    fanOut.OnEmpty = OnEmpty;
    fanOut.Logger = _logger;
    return fanOut;
  }

  public async ValueTask DisposeAsync()
  {
    lock (_lock)
    {
      if (_disposed) return;
      _disposed = true;
    }

    await StopRunAsync();

    if (_muxFanOut != null)
      await _muxFanOut.DisposeAsync();
    _muxSubscription?.Dispose();
    if (_dataFanOut != null)
      await _dataFanOut.DisposeAsync();
  }
}
