using Microsoft.Extensions.Logging;
using Shared.Models;
using Shared.Models.Entities;
using Shared.Models.Events;

namespace Server.Recording;

public sealed class SegmentWriter : IAsyncDisposable
{
  private const long FlushIntervalMicros = 60_000_000;

  private readonly Guid _cameraId;
  private readonly string _profile;
  private readonly string _storageProfile;
  private readonly string _codec;
  private readonly Guid _streamId;
  private readonly int _segmentDurationSeconds;
  private readonly IStorageProvider _storage;
  private readonly IDataProvider _data;
  private readonly IEventBus _eventBus;
  private readonly ILogger _logger;

  private readonly List<Keyframe> _pendingKeyframes = [];
  private TaskCompletionSource _sealTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
  private Guid _segmentId;
  private ISegmentHandle? _handle;
  private long _bytesWritten;
  private ulong _segmentStartTime;
  private ulong _lastTimestamp;
  private ulong _lastFlushTimestamp;
  private int _totalKeyframeCount;
  private string _fileExtension = "";
  private bool _disposed;

  public Action<Guid, long, ulong, ulong>? OnSegmentFinalized { get; set; }
  public Guid? ActiveSegmentId => _handle != null ? _segmentId : null;

  public SegmentWriter(
    Guid cameraId,
    string profile,
    string storageProfile,
    string codec,
    Guid streamId,
    int segmentDurationSeconds,
    IStorageProvider storage,
    IDataProvider data,
    IEventBus eventBus,
    ILogger logger)
  {
    _cameraId = cameraId;
    _profile = profile;
    _storageProfile = storageProfile;
    _codec = codec;
    _streamId = streamId;
    _segmentDurationSeconds = segmentDurationSeconds;
    _storage = storage;
    _data = data;
    _eventBus = eventBus;
    _logger = logger;
  }

  public void Seal() => _sealTcs.TrySetResult();

  public async Task<OneOf<Success, Error>> RunAsync(
    IMuxStream muxStream, ReadOnlyMemory<byte> header, CancellationToken ct)
  {
    _fileExtension = muxStream.Info.FileExtension;
    await using var enumerator = ReadAsDataUnits(muxStream, ct).GetAsyncEnumerator(ct);

    while (!ct.IsCancellationRequested)
    {
      var moveNext = enumerator.MoveNextAsync();
      if (!moveNext.IsCompleted)
      {
        var moveNextTask = moveNext.AsTask();
        var completed = await Task.WhenAny(moveNextTask, _sealTcs.Task);
        if (completed == _sealTcs.Task)
        {
          var sealedSegment = await FinalizeSegmentAsync(ct);
          if (sealedSegment.IsT1)
            return sealedSegment.AsT1;
          _sealTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

          if (!await moveNextTask)
            break;
        }
        else if (!await moveNextTask)
        {
          break;
        }
      }
      else if (!await moveNext)
      {
        break;
      }

      var fragment = enumerator.Current;

      if (_handle == null)
      {
        if (!fragment.IsSyncPoint)
          continue;

        var started = await StartSegmentAsync(fragment.Timestamp, header, ct);
        if (started.IsT1)
          return started.AsT1;
      }
      else if (fragment.IsSyncPoint)
      {
        if (ShouldFinalize(fragment.Timestamp))
        {
          _lastTimestamp = fragment.Timestamp - 1;
          var finalized = await FinalizeSegmentAsync(ct);
          if (finalized.IsT1)
            return finalized.AsT1;

          var started = await StartSegmentAsync(fragment.Timestamp, header, ct);
          if (started.IsT1)
            return started.AsT1;
        }
        else if (ShouldFlush(fragment.Timestamp))
        {
          await FlushAsync(ct);
        }
      }

      await WriteFragmentAsync(fragment, ct);
    }

    return await FinalizeSegmentAsync(ct);
  }

  private async Task<OneOf<Success, Error>> StartSegmentAsync(
    ulong timestamp, ReadOnlyMemory<byte> header, CancellationToken ct)
  {
    _segmentId = Guid.NewGuid();

    var metadata = new SegmentMetadata
    {
      CameraId = _cameraId,
      Profile = _storageProfile,
      StartTime = timestamp,
      Codec = _codec,
      FileExtension = _fileExtension
    };

    var created = await _storage.CreateSegmentAsync(metadata, ct);
    if (created.IsT1)
      return created.AsT1;

    _handle = created.AsT0;
    _segmentStartTime = timestamp;
    _lastFlushTimestamp = timestamp;
    _bytesWritten = 0;
    _totalKeyframeCount = 0;
    _pendingKeyframes.Clear();

    if (header.Length > 0)
    {
      await _handle.Stream.WriteAsync(header, ct);
      _bytesWritten += header.Length;
    }

    var segment = new Segment
    {
      Id = _segmentId,
      StreamId = _streamId,
      StartTime = timestamp,
      EndTime = timestamp,
      SegmentRef = _handle.SegmentRef,
      SizeBytes = _bytesWritten,
      KeyframeCount = 0
    };

    var createResult = await _data.Segments.CreateAsync(segment, ct);
    if (createResult.IsT1)
      _logger.LogError("Failed to create segment record: {Message}", createResult.AsT1.Message);

    _logger.LogDebug(
      "Started segment {SegmentId} for camera {CameraId} profile '{Profile}' at {Timestamp}",
      _segmentId, _cameraId, _profile, timestamp);
    return new Success();
  }

  private async Task WriteFragmentAsync(IDataUnit fragment, CancellationToken ct)
  {
    if (_handle == null)
      return;

    if (fragment.IsSyncPoint)
    {
      _pendingKeyframes.Add(new Keyframe
      {
        SegmentId = _segmentId,
        Timestamp = fragment.Timestamp,
        ByteOffset = _bytesWritten
      });
      _totalKeyframeCount++;
    }

    await _handle.Stream.WriteAsync(fragment.Data, ct);
    _bytesWritten += fragment.Data.Length;
    _lastTimestamp = fragment.Timestamp;
  }

  private bool ShouldFinalize(ulong currentTimestamp)
  {
    var elapsedMicros = currentTimestamp - _segmentStartTime;
    var targetMicros = (ulong)_segmentDurationSeconds * 1_000_000UL;
    return elapsedMicros >= targetMicros;
  }

  private bool ShouldFlush(ulong currentTimestamp)
  {
    return currentTimestamp - _lastFlushTimestamp >= FlushIntervalMicros;
  }

  private async Task FlushAsync(CancellationToken ct)
  {
    if (_handle == null || _pendingKeyframes.Count == 0)
      return;

    var kfResult = await _data.Keyframes.CreateBatchAsync(_pendingKeyframes, ct);
    if (kfResult.IsT1)
      _logger.LogError("Failed to flush keyframes: {Message}", kfResult.AsT1.Message);

    var segment = new Segment
    {
      Id = _segmentId,
      StreamId = _streamId,
      StartTime = _segmentStartTime,
      EndTime = _lastTimestamp,
      SegmentRef = _handle.SegmentRef,
      SizeBytes = _bytesWritten,
      KeyframeCount = _totalKeyframeCount
    };

    var updateResult = await _data.Segments.UpdateAsync(segment, ct);
    if (updateResult.IsT1)
      _logger.LogError("Failed to flush segment metadata: {Message}", updateResult.AsT1.Message);

    _pendingKeyframes.Clear();
    _lastFlushTimestamp = _lastTimestamp;
  }

  private async Task<OneOf<Success, Error>> FinalizeSegmentAsync(CancellationToken ct)
  {
    if (_handle == null)
      return new Success();

    var finalized = await _handle.FinalizeAsync(ct);
    if (finalized.IsT1)
      return finalized.AsT1;

    await FlushAsync(ct);

    await _eventBus.PublishAsync(new RecordingSegmentCompleted
    {
      CameraId = _cameraId,
      Profile = _profile,
      SegmentId = _segmentId,
      StartTime = _segmentStartTime,
      EndTime = _lastTimestamp,
      SizeBytes = _bytesWritten,
      Timestamp = DateTimeOffset.UtcNow.ToUnixMicroseconds()
    }, ct);

    OnSegmentFinalized?.Invoke(_streamId, _bytesWritten, _segmentStartTime, _lastTimestamp);

    _logger.LogInformation(
      "Finalized segment for camera {CameraId} profile '{Profile}': {Bytes} bytes, {Keyframes} keyframes, {Start}-{End}",
      _cameraId, _profile, _bytesWritten, _totalKeyframeCount, _segmentStartTime, _lastTimestamp);

    await _handle.DisposeAsync();
    _handle = null;
    return new Success();
  }

  private static IAsyncEnumerable<IDataUnit> ReadAsDataUnits(IMuxStream muxStream, CancellationToken ct) =>
    muxStream.ReadAsync(ct);

  public async ValueTask DisposeAsync()
  {
    if (_disposed) return;
    _disposed = true;

    if (_handle != null)
    {
      try
      {
        var finalized = await FinalizeSegmentAsync(CancellationToken.None);
        if (finalized.IsT1)
          _logger.LogError("Failed to finalize segment on dispose: {Message}", finalized.AsT1.Message);
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Failed to finalize segment on dispose");
      }
    }
  }
}
