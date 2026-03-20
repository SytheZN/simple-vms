using Microsoft.Extensions.Logging;
using Server.Plugins;
using Shared.Models;
using Shared.Models.Events;

namespace Server.Streaming;

public sealed class CameraPipeline : IAsyncDisposable
{
  private readonly Guid _cameraId;
  private readonly string _profile;
  private readonly CameraConnectionInfo _connectionInfo;
  private readonly ICaptureSource _captureSource;
  private readonly IPluginHost _pluginHost;
  private readonly IEventBus _eventBus;
  private readonly ILogger _logger;
  private readonly Lock _lock = new();

  private IStreamConnection? _connection;
  private IAsyncDisposable? _dataFanOut;
  private IAsyncDisposable? _videoFanOut;
  private CancellationTokenSource? _reconnectCts;
  private Task? _reconnectLoop;
  private bool _disposed;

  public Guid CameraId => _cameraId;
  public string Profile => _profile;
  public bool IsActive { get { lock (_lock) return _connection != null; } }

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
    CameraConnectionInfo connectionInfo,
    ICaptureSource captureSource,
    IPluginHost pluginHost,
    IEventBus eventBus,
    ILogger logger)
  {
    _cameraId = cameraId;
    _profile = profile;
    _connectionInfo = connectionInfo;
    _captureSource = captureSource;
    _pluginHost = pluginHost;
    _eventBus = eventBus;
    _logger = logger;
  }

  public async Task<OneOf<IDataStream, Error>> SubscribeDataAsync(CancellationToken ct)
  {
    lock (_lock)
    {
      if (_disposed)
        return Error.Create(ModuleIds.Streaming, 0x0002, Result.Unavailable,
          "Pipeline has been disposed");
    }

    if (!IsActive)
    {
      var result = await ActivateAsync(ct);
      if (result.IsT1)
        return result.AsT1;
    }

    lock (_lock)
    {
      if (_dataFanOut is not IDataStream fanOutStream)
        return Error.Create(ModuleIds.Streaming, 0x0003, Result.InternalError,
          "Data fan-out not available");

      var subscribeMethod = _dataFanOut.GetType().GetMethod("Subscribe");
      if (subscribeMethod == null)
        return Error.Create(ModuleIds.Streaming, 0x0004, Result.InternalError,
          "Data fan-out does not support Subscribe");

      return OneOf<IDataStream, Error>.FromT0((IDataStream)subscribeMethod.Invoke(_dataFanOut, [256])!);
    }
  }

  public async Task<OneOf<Success, Error>> ActivateAsync(CancellationToken ct)
  {
    lock (_lock)
    {
      if (_connection != null)
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

    var fanOut = CreateTypedFanOut(dataStream);
    if (fanOut == null)
    {
      await connection.DisposeAsync();
      return Error.Create(ModuleIds.Streaming, 0x0011, Result.InternalError,
        "Failed to create data stream fan-out");
    }

    var format = _pluginHost.FindFormat(dataStream.FrameType);
    if (format != null)
    {
      var pipelineResult = format.CreatePipeline(dataStream, connection.Info);
      if (pipelineResult.IsT0)
      {
        _logger.LogInformation("Video pipeline created for camera {CameraId} profile '{Profile}'",
          _cameraId, _profile);
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

    lock (_lock)
    {
      _connection = connection;
      _dataFanOut = fanOut;
    }

    StartReconnectWatch(connection);

    await _eventBus.PublishAsync(new CameraStatusChanged
    {
      CameraId = _cameraId,
      Status = "online",
      Timestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000
    }, ct);

    await _eventBus.PublishAsync(new StreamStarted
    {
      CameraId = _cameraId,
      Profile = _profile,
      DataFormat = connection.Info.DataFormat,
      Timestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000
    }, ct);

    _logger.LogInformation("Activated pipeline for camera {CameraId} profile '{Profile}'",
      _cameraId, _profile);

    return new Success();
  }

  public async Task DeactivateAsync()
  {
    CancellationTokenSource? reconnectCts;
    Task? reconnectLoop;

    lock (_lock)
    {
      reconnectCts = _reconnectCts;
      _reconnectCts = null;
      reconnectLoop = _reconnectLoop;
      _reconnectLoop = null;
    }

    if (reconnectCts != null)
    {
      reconnectCts.Cancel();
      if (reconnectLoop != null)
      {
        try { await reconnectLoop; }
        catch { /* swallow */ }
      }
      reconnectCts.Dispose();
    }

    await TeardownConnectionAsync();

    _logger.LogInformation("Deactivated pipeline for camera {CameraId} profile '{Profile}'",
      _cameraId, _profile);
  }

  private async Task TeardownConnectionAsync()
  {
    IAsyncDisposable? videoFanOut;
    IAsyncDisposable? dataFanOut;
    IStreamConnection? connection;

    lock (_lock)
    {
      videoFanOut = _videoFanOut;
      _videoFanOut = null;
      dataFanOut = _dataFanOut;
      _dataFanOut = null;
      connection = _connection;
      _connection = null;
    }

    if (videoFanOut != null)
      await videoFanOut.DisposeAsync();
    if (dataFanOut != null)
      await dataFanOut.DisposeAsync();
    if (connection != null)
      await connection.DisposeAsync();
  }

  private void StartReconnectWatch(IStreamConnection connection)
  {
    var cts = new CancellationTokenSource();
    lock (_lock)
    {
      _reconnectCts = cts;
    }

    _reconnectLoop = Task.Run(async () =>
    {
      try
      {
        var cancelled = new TaskCompletionSource();
        using var reg = cts.Token.Register(() => cancelled.TrySetResult());
        await Task.WhenAny(connection.Completed, cancelled.Task);
      }
      catch { /* connection failed */ }

      if (cts.Token.IsCancellationRequested)
        return;

      await TeardownConnectionAsync();

      await _eventBus.PublishAsync(new CameraStatusChanged
      {
        CameraId = _cameraId,
        Status = "offline",
        Reason = "disconnected",
        Timestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000
      }, CancellationToken.None);

      await _eventBus.PublishAsync(new StreamStopped
      {
        CameraId = _cameraId,
        Profile = _profile,
        Reason = "disconnected",
        Timestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000
      }, CancellationToken.None);

      await ReconnectLoopAsync(cts.Token);
    });
  }

  private async Task ReconnectLoopAsync(CancellationToken ct)
  {
    var backoffIndex = 0;

    while (!ct.IsCancellationRequested)
    {
      var delay = BackoffDelays[Math.Min(backoffIndex, BackoffDelays.Length - 1)];
      _logger.LogDebug("Reconnecting camera {CameraId} profile '{Profile}' in {Delay}s",
        _cameraId, _profile, delay.TotalSeconds);

      try
      {
        await Task.Delay(delay, ct);
      }
      catch (OperationCanceledException)
      {
        break;
      }

      var result = await ActivateAsync(ct);
      if (result.IsT0)
      {
        _logger.LogInformation("Reconnected camera {CameraId} profile '{Profile}'",
          _cameraId, _profile);
        return;
      }

      backoffIndex++;
    }
  }

  private IAsyncDisposable? CreateTypedFanOut(IDataStream dataStream)
  {
    var frameType = dataStream.FrameType;
    var fanOutType = typeof(DataStreamFanOut<>).MakeGenericType(frameType);
    var fanOut = (IAsyncDisposable)Activator.CreateInstance(fanOutType, dataStream)!;

    var onEmptyProp = fanOutType.GetProperty("OnEmpty");
    onEmptyProp?.SetValue(fanOut, new Action(OnFanOutEmpty));

    var startMethod = fanOutType.GetMethod("Start");
    startMethod?.Invoke(fanOut, null);

    return fanOut;
  }

  private void OnFanOutEmpty()
  {
    _ = Task.Run(async () =>
    {
      _logger.LogDebug("No subscribers remaining for camera {CameraId} profile '{Profile}'",
        _cameraId, _profile);
      await DeactivateAsync();
    });
  }

  public async ValueTask DisposeAsync()
  {
    lock (_lock)
    {
      if (_disposed) return;
      _disposed = true;
    }

    await DeactivateAsync();
  }
}
