using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Server.Plugins;
using Shared.Models;
using Shared.Models.Events;

namespace Server.Streaming;

public sealed class CameraPipeline : IPipeline
{
  private readonly Guid _cameraId;
  private readonly string _profile;
  private readonly string? _expectedCodec;
  private readonly CameraConnectionInfo _connectionInfo;
  private readonly ICaptureSource _captureSource;
  private readonly IPluginHost _pluginHost;
  private readonly IEventBus _eventBus;
  private readonly ILogger _logger;
  private readonly Lock _lock = new();
  private readonly DemandEvaluator _evaluator;

  private IDataStreamFanOut? _dataFanOut;
  private IMuxStreamFanOut? _muxFanOut;
  private IDisposable? _muxSubscription;
  private IStreamConnection? _connection;
  private CancellationTokenSource? _feedCts;
  private Task? _feedLoop;
  private bool _connecting;
  private bool _reconnecting;
  private bool _constructed;
  private bool _disposed;

  public Guid CameraId => _cameraId;
  public string Profile => _profile;
  public string ConnectionUri => _connectionInfo.Uri;
  public string? ExpectedCodec => _expectedCodec;
  public bool IsConstructed { get { lock (_lock) return _constructed; } }
  public bool Recordable => true;
  public bool IsActive { get { lock (_lock) return _connection != null; } }
  public MuxStreamInfo? MuxInfo { get { lock (_lock) return _muxFanOut?.Info; } }

  public Action<MuxStreamStats>? OnStats
  {
    set { lock (_lock) { if (_muxFanOut != null) _muxFanOut.OnStats = value; } }
  }
  public ReadOnlyMemory<byte> MuxHeader { get { lock (_lock) return _muxFanOut?.Header ?? ReadOnlyMemory<byte>.Empty; } }

  internal TimeSpan DisconnectLinger { get; set; } = TimeSpan.FromSeconds(5);

  internal static readonly TimeSpan[] BackoffDelays =
  [
    TimeSpan.FromSeconds(1),
    TimeSpan.FromSeconds(2),
    TimeSpan.FromSeconds(4),
    TimeSpan.FromSeconds(8),
    TimeSpan.FromSeconds(16),
    TimeSpan.FromSeconds(30)
  ];

  public CameraPipeline(
    Guid cameraId,
    string profile,
    string? expectedCodec,
    CameraConnectionInfo connectionInfo,
    ICaptureSource captureSource,
    IPluginHost pluginHost,
    IEventBus eventBus,
    ILogger logger)
  {
    _cameraId = cameraId;
    _profile = profile;
    _expectedCodec = expectedCodec;
    _connectionInfo = connectionInfo;
    _captureSource = captureSource;
    _pluginHost = pluginHost;
    _eventBus = eventBus;
    _logger = logger;
    _evaluator = new DemandEvaluator(EvaluateOnceAsync,
      ex => _logger.LogError(ex, "Demand evaluation failed for camera {CameraId} profile '{Profile}'",
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
    {
      if (_constructed)
        return new Success();
    }

    var connectResult = await _captureSource.ConnectAsync(_connectionInfo, ct);
    if (connectResult.IsT1)
    {
      _logger.LogError("Connect failed for camera {CameraId}: {Message}",
        _cameraId, connectResult.AsT1.Message);
      return connectResult.AsT1;
    }

    var connection = connectResult.AsT0;
    var dataStream = connection.DataStream;

    var fanOut = CreateDataFanOut(dataStream);
    var muxInput = fanOut.SubscribePassive(256);
    var muxSub = muxInput as IDisposable;

    StartFeeding(connection, fanOut, dataStream);

    IMuxStreamFanOut? muxFanOut = null;
    var format = _pluginHost.FindFormat(dataStream.FrameType);
    if (format != null)
    {
      var pipelineResult = await format.CreatePipelineAsync(muxInput, connection.Info, ct);
      if (pipelineResult.IsT0)
      {
        var muxStream = pipelineResult.AsT0;
        muxFanOut = CreateMuxFanOut(muxStream);
        _logger.LogInformation(
          "Pipeline constructed for camera {CameraId} profile '{Profile}', mime={MimeType}",
          _cameraId, _profile, muxStream.Info.MimeType);
      }
      else
      {
        _logger.LogWarning("Format pipeline failed for camera {CameraId}: {Message}",
          _cameraId, pipelineResult.AsT1.Message);
      }
    }
    else
    {
      _logger.LogWarning("No matching format plugin for {FrameType} on camera {CameraId}",
        dataStream.FrameType.Name, _cameraId);
    }

    await StopFeeding();
    await connection.DisposeAsync();

    lock (_lock)
    {
      _dataFanOut = fanOut;
      _muxFanOut = muxFanOut;
      _muxSubscription = muxSub;
      _constructed = true;
    }

    Evaluate();
    return new Success();
  }

  public async Task<OneOf<IDataStream, Error>> SubscribeDataAsync(CancellationToken ct)
  {
    lock (_lock)
    {
      if (_disposed)
        return Error.Create(ModuleIds.Streaming, 0x0002, Result.Unavailable,
          "Pipeline has been disposed");
      if (!_constructed)
        return Error.Create(ModuleIds.Streaming, 0x0003, Result.Unavailable,
          "Pipeline not constructed");
    }

    lock (_lock)
      return OneOf<IDataStream, Error>.FromT0(_dataFanOut!.Subscribe(256));
  }

  public async Task<OneOf<IMuxStream, Error>> SubscribeMuxAsync(CancellationToken ct)
  {
    lock (_lock)
    {
      if (_disposed)
        return Error.Create(ModuleIds.Streaming, 0x0005, Result.Unavailable,
          "Pipeline has been disposed");
      if (!_constructed)
        return Error.Create(ModuleIds.Streaming, 0x0006, Result.Unavailable,
          "Pipeline not constructed");
      if (_muxFanOut == null)
        return Error.Create(ModuleIds.Streaming, 0x0007, Result.Unavailable,
          "No video pipeline available");

      return OneOf<IMuxStream, Error>.FromT0(_muxFanOut.Subscribe(256));
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
    bool connected;
    lock (_lock)
      connected = _connection != null;

    if (want && !connected)
    {
      await ConnectSourceAsync(CancellationToken.None);
      lock (_lock)
        connected = _connection != null;
      if (!connected && GetDemand() > 0)
        StartReconnectLoop();
    }
    else if (!want && connected)
    {
      await Task.Delay(DisconnectLinger);
      if (GetDemand() == 0)
        await DisconnectSourceAsync();
    }
  }

  private async Task ConnectSourceAsync(CancellationToken ct)
  {
    lock (_lock)
    {
      if (_connection != null || _connecting || !_constructed || _disposed)
        return;
      _connecting = true;
    }

    try
    {
      var connectResult = await _captureSource.ConnectAsync(_connectionInfo, ct);
      if (connectResult.IsT1)
      {
        _logger.LogError("Connect failed for camera {CameraId}: {Message}",
          _cameraId, connectResult.AsT1.Message);
        return;
      }

      var connection = connectResult.AsT0;

      if (_dataFanOut is IDataStream fanOut && fanOut.FrameType != connection.DataStream.FrameType)
      {
        _logger.LogDebug(
          "Refusing connection for camera {CameraId} profile '{Profile}': stream carries {Actual}, pipeline carries {Expected}",
          _cameraId, _profile, connection.DataStream.FrameType.Name, fanOut.FrameType.Name);
        await connection.DisposeAsync();
        return;
      }

      StartFeeding(connection, _dataFanOut!, connection.DataStream);

      lock (_lock)
        _connection = connection;

      WatchConnection(connection);

      await _eventBus.PublishAsync(new CameraStatusChanged
      {
        CameraId = _cameraId,
        Profile = _profile,
        Status = "online",
        Timestamp = DateTimeOffset.UtcNow.ToUnixMicroseconds()
      }, ct);

      await _eventBus.PublishAsync(new StreamStarted
      {
        CameraId = _cameraId,
        Profile = _profile,
        DataFormat = connection.Info.DataFormat,
        Timestamp = DateTimeOffset.UtcNow.ToUnixMicroseconds()
      }, ct);

      _logger.LogInformation("Source connected for camera {CameraId} profile '{Profile}'",
        _cameraId, _profile);
    }
    finally
    {
      lock (_lock)
        _connecting = false;
    }
  }

  private async Task DisconnectSourceAsync()
  {
    IStreamConnection? connection;
    lock (_lock)
    {
      connection = _connection;
      _connection = null;
    }

    await StopFeeding();

    if (connection != null)
    {
      await connection.DisposeAsync();

      await _eventBus.PublishAsync(new CameraStatusChanged
      {
        CameraId = _cameraId,
        Profile = _profile,
        Status = "offline",
        Reason = "no demand",
        Timestamp = DateTimeOffset.UtcNow.ToUnixMicroseconds()
      }, CancellationToken.None);

      _logger.LogInformation("Source disconnected for camera {CameraId} profile '{Profile}'",
        _cameraId, _profile);
    }
  }

  private void StartFeeding(IStreamConnection connection, IDataStreamFanOut fanOut, IDataStream dataStream)
  {
    var cts = new CancellationTokenSource();

    lock (_lock)
      _feedCts = cts;

    _feedLoop = Task.Run(async () =>
    {
      try
      {
        await foreach (var item in dataStream.ReadAsync(cts.Token))
          fanOut.Write(item);
      }
      catch (OperationCanceledException) { }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Feed loop failed for camera {CameraId} profile '{Profile}'",
          _cameraId, _profile);
      }
    });
  }

  private async Task StopFeeding()
  {
    CancellationTokenSource? cts;
    Task? loop;
    lock (_lock)
    {
      cts = _feedCts;
      _feedCts = null;
      loop = _feedLoop;
      _feedLoop = null;
    }

    if (cts != null)
    {
      cts.Cancel();
      if (loop != null)
      {
        try { await loop.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing); }
        catch { }
      }
      cts.Dispose();
    }
  }

  private void WatchConnection(IStreamConnection connection)
  {
    _ = Task.Run(async () =>
    {
      try
      {
        await connection.Completed;
      }
      catch { }

      bool wasConnected;
      lock (_lock)
        wasConnected = _connection == connection;

      if (!wasConnected)
        return;

      _logger.LogDebug("Connection lost for camera {CameraId} profile '{Profile}'",
        _cameraId, _profile);

      await DisconnectSourceAsync();

      await _eventBus.PublishAsync(new StreamStopped
      {
        CameraId = _cameraId,
        Profile = _profile,
        Reason = "disconnected",
        Timestamp = DateTimeOffset.UtcNow.ToUnixMicroseconds()
      }, CancellationToken.None);

      StartReconnectLoop();
    });
  }

  private void StartReconnectLoop()
  {
    lock (_lock)
    {
      if (_disposed || _reconnecting)
        return;
      _reconnecting = true;
    }

    _ = Task.Run(async () =>
    {
      try
      {
        await ReconnectAsync();
      }
      finally
      {
        lock (_lock)
          _reconnecting = false;
      }
    });
  }

  private async Task ReconnectAsync()
  {
    var backoffIndex = 0;

    while (!_disposed)
    {
      if (GetDemand() == 0)
        return;

      var delay = BackoffDelays[Math.Min(backoffIndex, BackoffDelays.Length - 1)];
      _logger.LogDebug("Reconnecting camera {CameraId} profile '{Profile}' in {Delay}s",
        _cameraId, _profile, delay.TotalSeconds);

      await Task.Delay(delay);

      lock (_lock)
      {
        if (_connection != null)
          return;
      }

      await ConnectSourceAsync(CancellationToken.None);

      lock (_lock)
      {
        if (_connection != null)
        {
          _logger.LogInformation("Reconnected camera {CameraId} profile '{Profile}'",
            _cameraId, _profile);
          return;
        }
      }

      backoffIndex++;
    }
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

    await DisconnectSourceAsync();

    if (_muxFanOut != null)
      await _muxFanOut.DisposeAsync();
    _muxSubscription?.Dispose();
    if (_dataFanOut != null)
      await _dataFanOut.DisposeAsync();
  }
}
