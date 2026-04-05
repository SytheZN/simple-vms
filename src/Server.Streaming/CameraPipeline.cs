using System.Diagnostics.CodeAnalysis;
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

  private IDataStreamFanOut? _dataFanOut;
  private IVideoStreamFanOut? _videoFanOut;
  private IStreamConnection? _connection;
  private CancellationTokenSource? _feedCts;
  private Task? _feedLoop;
  private Type? _constructedFrameType;
  private bool _constructed;
  private bool _disposed;

  public Guid CameraId => _cameraId;
  public string Profile => _profile;
  public string ConnectionUri => _connectionInfo.Uri;
  public bool IsConstructed { get { lock (_lock) return _constructed; } }
  public bool IsActive { get { lock (_lock) return _connection != null; } }
  public VideoStreamInfo? VideoInfo { get { lock (_lock) return _videoFanOut?.Info; } }
  public ReadOnlyMemory<byte> VideoHeader { get { lock (_lock) return _videoFanOut?.Header ?? ReadOnlyMemory<byte>.Empty; } }

  public Action? OnParameterMismatch { get; set; }

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

    StartFeeding(connection, fanOut, dataStream);

    IVideoStreamFanOut? videoFanOut = null;
    var format = _pluginHost.FindFormat(dataStream.FrameType);
    if (format != null)
    {
      var pipelineResult = await format.CreatePipelineAsync(muxInput, connection.Info, ct);
      if (pipelineResult.IsT0)
      {
        var videoStream = pipelineResult.AsT0;
        videoFanOut = CreateVideoFanOut(videoStream);
        _logger.LogInformation(
          "Pipeline constructed for camera {CameraId} profile '{Profile}', mime={MimeType}",
          _cameraId, _profile, videoStream.Info.MimeType);
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
      _videoFanOut = videoFanOut;
      _constructedFrameType = dataStream.FrameType;
      _constructed = true;
    }

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

  public async Task<OneOf<IVideoStream, Error>> SubscribeVideoAsync(CancellationToken ct)
  {
    lock (_lock)
    {
      if (_disposed)
        return Error.Create(ModuleIds.Streaming, 0x0005, Result.Unavailable,
          "Pipeline has been disposed");
      if (!_constructed)
        return Error.Create(ModuleIds.Streaming, 0x0006, Result.Unavailable,
          "Pipeline not constructed");
      if (_videoFanOut == null)
        return Error.Create(ModuleIds.Streaming, 0x0007, Result.Unavailable,
          "No video pipeline available");

      return OneOf<IVideoStream, Error>.FromT0(_videoFanOut.Subscribe(256));
    }
  }

  private void OnDemand()
  {
    _ = Task.Run(async () =>
    {
      _logger.LogDebug("Demand signaled for camera {CameraId} profile '{Profile}'",
        _cameraId, _profile);
      await ConnectSourceAsync(CancellationToken.None);
    });
  }

  private void OnEmpty()
  {
    _ = Task.Run(async () =>
    {
      _logger.LogDebug("No demand for camera {CameraId} profile '{Profile}'",
        _cameraId, _profile);
      await DisconnectSourceAsync();
    });
  }

  private async Task ConnectSourceAsync(CancellationToken ct)
  {
    lock (_lock)
    {
      if (_connection != null || !_constructed)
        return;
    }

    var connectResult = await _captureSource.ConnectAsync(_connectionInfo, ct);
    if (connectResult.IsT1)
    {
      _logger.LogError("Connect failed for camera {CameraId}: {Message}",
        _cameraId, connectResult.AsT1.Message);
      return;
    }

    var connection = connectResult.AsT0;

    if (connection.DataStream.FrameType != _constructedFrameType)
    {
      _logger.LogWarning(
        "Stream parameter mismatch for camera {CameraId} profile '{Profile}': expected {Expected}, got {Actual}",
        _cameraId, _profile, _constructedFrameType?.Name, connection.DataStream.FrameType.Name);
      await connection.DisposeAsync();
      OnParameterMismatch?.Invoke();
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
        try { await loop; }
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

      await ReconnectAsync();
    });
  }

  private async Task ReconnectAsync()
  {
    var backoffIndex = 0;

    while (!_disposed)
    {
      bool hasDemand;
      lock (_lock)
      {
        hasDemand = (_dataFanOut?.SubscriberCount ?? 0) > 0
          || (_videoFanOut?.SubscriberCount ?? 0) > 0;
      }

      if (!hasDemand)
        return;

      var delay = BackoffDelays[Math.Min(backoffIndex, BackoffDelays.Length - 1)];
      _logger.LogDebug("Reconnecting camera {CameraId} profile '{Profile}' in {Delay}s",
        _cameraId, _profile, delay.TotalSeconds);

      try
      {
        await Task.Delay(delay);
      }
      catch (OperationCanceledException)
      {
        break;
      }

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
    fanOut.OnDemand = OnDemand;
    fanOut.OnEmpty = OnEmpty;
    fanOut.Logger = _logger;
    return fanOut;
  }

  [RequiresDynamicCode("Fan-out generic type is constructed at runtime")]
  private IVideoStreamFanOut CreateVideoFanOut(IVideoStream videoStream)
  {
    var fanOutType = typeof(VideoStreamFanOut<>).MakeGenericType(videoStream.FrameType);
    var fanOut = (IVideoStreamFanOut)Activator.CreateInstance(fanOutType, videoStream)!;
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

    await DisconnectSourceAsync();

    if (_videoFanOut != null)
      await _videoFanOut.DisposeAsync();
    if (_dataFanOut != null)
      await _dataFanOut.DisposeAsync();
  }
}
