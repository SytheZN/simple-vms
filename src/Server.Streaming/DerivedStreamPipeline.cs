using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
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
  private readonly bool _recordable;
  private readonly ILogger _logger;
  private readonly Lock _lock = new();
  private readonly DemandEvaluator _evaluator;

  private IDataStreamFanOut? _dataFanOut;
  private IMuxStreamFanOut? _muxFanOut;
  private IDisposable? _muxSubscription;
  private CancellationTokenSource? _runCts;
  private Task? _feedLoop;
  private IDataStream? _runStream;
  private Type? _frameType;
  private int _startFailures;
  private bool _constructed;
  private bool _disposed;

  internal static readonly TimeSpan[] StartRetryDelays =
  [
    TimeSpan.FromSeconds(1),
    TimeSpan.FromSeconds(2),
    TimeSpan.FromSeconds(4),
    TimeSpan.FromSeconds(8),
    TimeSpan.FromSeconds(15),
    TimeSpan.FromSeconds(30)
  ];

  public Guid CameraId => _cameraId;
  public string Profile => _profile;
  public string ParentProfile => _parentProfile;
  public string ProducerId => _analyzerIdentity.AnalyzerId;
  public string FormatId => _format.FormatId;
  public bool IsConstructed { get { lock (_lock) return _constructed; } }
  public bool Recordable => _recordable;
  public bool IsRunning { get { lock (_lock) return _runCts != null; } }
  public bool NeedsRebuild => _analyzer.NeedsRebuild(_cameraId, _parentProfile);
  public ReadOnlyMemory<byte> MuxHeader { get { lock (_lock) return _muxFanOut?.Header ?? ReadOnlyMemory<byte>.Empty; } }
  public MuxStreamInfo? MuxInfo { get { lock (_lock) return _muxFanOut?.Info; } }

  public Action<MuxStreamStats>? OnStats
  {
    set { lock (_lock) { if (_muxFanOut != null) _muxFanOut.OnStats = value; } }
  }

  public DerivedStreamPipeline(
    Guid cameraId,
    string profile,
    string parentProfile,
    IDataStreamAnalyzer analyzerIdentity,
    IDataStreamAnalyzerStreamOutput analyzer,
    IStreamFormat format,
    bool recordable,
    ILogger logger)
  {
    _cameraId = cameraId;
    _profile = profile;
    _parentProfile = parentProfile;
    _analyzerIdentity = analyzerIdentity;
    _analyzer = analyzer;
    _format = format;
    _recordable = recordable;
    _logger = logger;
    _evaluator = new DemandEvaluator(EvaluateOnceAsync,
      ex => _logger.LogError(ex, "Demand evaluation failed for derived stream {CameraId}/{Profile}",
        _cameraId, _profile));
  }

  public int GetDemand()
  {
    IDataStreamFanOut? dataFanOut;
    IMuxStreamFanOut? muxFanOut;
    lock (_lock)
    {
      dataFanOut = _dataFanOut;
      muxFanOut = _muxFanOut;
    }
    return (dataFanOut?.GetDemand() ?? 0) + (muxFanOut?.GetDemand() ?? 0);
  }

  public void Evaluate() => _evaluator.Schedule();

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

    var probeFeed = Task.Run(async () =>
    {
      try
      {
        await foreach (var item in dataStream.ReadAsync(probeCts.Token))
          fanOut.Write(item);
      }
      catch (OperationCanceledException) { }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Probe feed failed for derived stream {CameraId}/{Profile}",
          _cameraId, _profile);
      }
    });

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
    await probeFeed.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

    if (dataStream is IAsyncDisposable probeDisposable)
      await probeDisposable.DisposeAsync();

    lock (_lock)
    {
      _dataFanOut = fanOut;
      _muxFanOut = muxFanOut;
      _muxSubscription = muxSub;
      _frameType = dataStream.FrameType;
      _constructed = true;
    }

    Evaluate();
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

  private async Task EvaluateOnceAsync()
  {
    lock (_lock)
    {
      if (_disposed || !_constructed)
        return;
    }

    var want = GetDemand() > 0;
    bool running;
    lock (_lock)
      running = _runCts != null;

    if (want && !running)
    {
      var started = await StartRunAsync();
      if (started)
      {
        _startFailures = 0;
      }
      else
      {
        var delay = StartRetryDelays[Math.Min(_startFailures, StartRetryDelays.Length - 1)];
        _startFailures++;
        _ = Task.Run(async () =>
        {
          await Task.Delay(delay);
          Evaluate();
        });
      }
    }
    else if (!want && running)
    {
      await StopRunAsync();
    }
  }

  private async Task<bool> StartRunAsync()
  {
    CancellationTokenSource cts;
    lock (_lock)
    {
      if (_disposed || _runCts != null) return true;
      cts = _runCts = new CancellationTokenSource();
    }

    var startResult = await _analyzer.StartStreamAsync(_cameraId, _parentProfile, cts.Token);
    if (startResult.IsT1)
    {
      _logger.LogWarning("Analyzer {AnalyzerId} StartAsync failed for {CameraId}/{Profile}: {Message}",
        _analyzerIdentity.AnalyzerId, _cameraId, _profile, startResult.AsT1.Message);
      lock (_lock) _runCts = null;
      cts.Dispose();
      return false;
    }

    var stream = startResult.AsT0;
    if (stream.FrameType != _frameType)
    {
      _logger.LogWarning("Analyzer {AnalyzerId} returned stream of {Actual}, expected {Expected}",
        _analyzerIdentity.AnalyzerId, stream.FrameType.Name, _frameType?.Name);
      lock (_lock) _runCts = null;
      cts.Dispose();
      if (stream is IAsyncDisposable disposable)
        await disposable.DisposeAsync();
      return false;
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
      finally
      {
        OnRunEnded(cts, stream);
      }
    });

    lock (_lock)
    {
      if (_runCts == cts)
      {
        _feedLoop = feed;
        _runStream = stream;
      }
    }

    return true;
  }

  private void OnRunEnded(CancellationTokenSource cts, IDataStream stream)
  {
    bool current;
    lock (_lock)
    {
      current = _runCts == cts;
      if (current)
      {
        _runCts = null;
        _feedLoop = null;
        _runStream = null;
      }
    }

    if (!current) return;

    cts.Dispose();
    _ = Task.Run(async () =>
    {
      if (stream is IAsyncDisposable disposable)
        await disposable.DisposeAsync();
    });

    _logger.LogDebug("Run ended for derived stream {CameraId}/{Profile}", _cameraId, _profile);
    Evaluate();
  }

  private async Task StopRunAsync()
  {
    CancellationTokenSource? cts;
    Task? loop;
    IDataStream? stream;
    lock (_lock)
    {
      cts = _runCts;
      _runCts = null;
      loop = _feedLoop;
      _feedLoop = null;
      stream = _runStream;
      _runStream = null;
    }

    if (cts != null)
    {
      cts.Cancel();
      if (loop != null)
        await loop.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
      cts.Dispose();
    }

    if (stream is IAsyncDisposable disposable)
      await disposable.DisposeAsync();
  }

  [RequiresDynamicCode("Fan-out generic type is constructed at runtime")]
  private IDataStreamFanOut CreateDataFanOut(IDataStream dataStream)
  {
    var fanOutType = typeof(DataStreamFanOut<>).MakeGenericType(dataStream.FrameType);
    var fanOut = (IDataStreamFanOut)Activator.CreateInstance(fanOutType, dataStream.Info)!;
    fanOut.Changed = Evaluate;
    fanOut.Logger = _logger;
    return fanOut;
  }

  [RequiresDynamicCode("Fan-out generic type is constructed at runtime")]
  private IMuxStreamFanOut CreateMuxFanOut(IMuxStream muxStream)
  {
    var fanOutType = typeof(MuxStreamFanOut<>).MakeGenericType(muxStream.FrameType);
    var fanOut = (IMuxStreamFanOut)Activator.CreateInstance(fanOutType, muxStream)!;
    fanOut.Changed = Evaluate;
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
