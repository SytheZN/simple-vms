using System.Runtime.Loader;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Server.Plugins;
using Server.Recording;
using Tests.Unit.Mocks;

namespace Tests.Unit.Recording;

[TestFixture]
public class RetentionEngineTests
{
  private const long Gb = 1024L * 1024 * 1024;
  private const ulong Hour = 3_600_000_000UL;

  /// <summary>
  /// SCENARIO:
  /// Stream has RetentionMode.Days with value 7; camera has RetentionMode.Default; global is Days/30
  ///
  /// ACTION:
  /// Resolve policy
  ///
  /// EXPECTED RESULT:
  /// Stream policy wins: Days/7
  /// </summary>
  [Test]
  public void ResolvePolicy_StreamOverrideWins()
  {
    var stream = MakeStream(RetentionMode.Days, 7);
    var camera = MakeCamera(RetentionMode.Default, 0);

    var (mode, value) = RetentionEngine.ResolvePolicy(stream, camera, (RetentionMode.Days, 30));

    Assert.That(mode, Is.EqualTo(RetentionMode.Days));
    Assert.That(value, Is.EqualTo(7));
  }

  /// <summary>
  /// SCENARIO:
  /// Stream has RetentionMode.Default; camera has RetentionMode.Bytes/1000000
  ///
  /// ACTION:
  /// Resolve policy
  ///
  /// EXPECTED RESULT:
  /// Camera policy used: Bytes/1000000
  /// </summary>
  [Test]
  public void ResolvePolicy_CameraFallback()
  {
    var stream = MakeStream(RetentionMode.Default, 0);
    var camera = MakeCamera(RetentionMode.Bytes, 1_000_000);

    var (mode, value) = RetentionEngine.ResolvePolicy(stream, camera, (RetentionMode.Days, 30));

    Assert.That(mode, Is.EqualTo(RetentionMode.Bytes));
    Assert.That(value, Is.EqualTo(1_000_000));
  }

  /// <summary>
  /// SCENARIO:
  /// Stream and camera both have RetentionMode.Default
  ///
  /// ACTION:
  /// Resolve policy
  ///
  /// EXPECTED RESULT:
  /// Global default used: Percent/80
  /// </summary>
  [Test]
  public void ResolvePolicy_GlobalFallback()
  {
    var stream = MakeStream(RetentionMode.Default, 0);
    var camera = MakeCamera(RetentionMode.Default, 0);

    var (mode, value) = RetentionEngine.ResolvePolicy(stream, camera, (RetentionMode.Percent, 80));

    Assert.That(mode, Is.EqualTo(RetentionMode.Percent));
    Assert.That(value, Is.EqualTo(80));
  }

  /// <summary>
  /// SCENARIO:
  /// Retention mode is Days/7; three segments exist: 10 days old, 5 days old, 1 day old
  ///
  /// ACTION:
  /// Run retention evaluation
  ///
  /// EXPECTED RESULT:
  /// Only the 10-day-old segment is purged; the other two remain
  /// </summary>
  [Test]
  public async Task PurgeByDays_DeletesOldSegments()
  {
    var now = (ulong)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000);
    var day = 86_400_000_000UL;

    var streamId = Guid.NewGuid();
    var seg10 = MakeSegment(streamId, now - 10 * day, now - 10 * day + 1000);
    var seg5 = MakeSegment(streamId, now - 5 * day, now - 5 * day + 1000);
    var seg1 = MakeSegment(streamId, now - 1 * day, now - 1 * day + 1000);

    var data = new FakeDataProvider();
    var stream = MakeStream(RetentionMode.Days, 7, streamId);
    var camera = MakeCamera(RetentionMode.Default, 0);

    data.AddCamera(camera);
    data.AddStream(stream);
    data.AddSegments(streamId, [seg10, seg5, seg1]);

    var storage = new FakeStorage();
    var engine = CreateEngine(data, storage);

    await engine.EvaluateAsync(storage, CancellationToken.None);

    Assert.That(storage.PurgedRefs, Has.Count.EqualTo(1));
    Assert.That(storage.PurgedRefs[0], Is.EqualTo(seg10.SegmentRef));
    Assert.That(data.DeletedSegmentIds, Has.Count.EqualTo(1));
    Assert.That(data.DeletedSegmentIds[0], Is.EqualTo(seg10.Id));
  }

  /// <summary>
  /// SCENARIO:
  /// Retention mode is Bytes/100; three segments of 50 bytes each (total 150)
  ///
  /// ACTION:
  /// Run retention evaluation
  ///
  /// EXPECTED RESULT:
  /// Oldest segment is purged (bringing total to 100); other two remain
  /// </summary>
  [Test]
  public async Task PurgeByBytes_DeletesOldestUntilUnderLimit()
  {
    var streamId = Guid.NewGuid();
    var now = Now();
    var seg1 = MakeSegment(streamId, now - 3 * Hour, now - 2 * Hour, size: 50);
    var seg2 = MakeSegment(streamId, now - 2 * Hour, now - Hour, size: 50);
    var seg3 = MakeSegment(streamId, now - Hour, now, size: 50);

    var data = new FakeDataProvider();
    var stream = MakeStream(RetentionMode.Bytes, 100, streamId);
    var camera = MakeCamera(RetentionMode.Default, 0);

    data.AddCamera(camera);
    data.AddStream(stream);
    data.AddSegments(streamId, [seg1, seg2, seg3]);

    var storage = new FakeStorage();
    var engine = CreateEngine(data, storage);

    await engine.EvaluateAsync(storage, CancellationToken.None);

    Assert.That(storage.PurgedRefs, Has.Count.EqualTo(1));
    Assert.That(storage.PurgedRefs[0], Is.EqualTo(seg1.SegmentRef));
  }

  /// <summary>
  /// SCENARIO:
  /// Soft-deleted stream has no remaining segments; an IPluginStreamSettings is registered
  ///
  /// ACTION:
  /// Run retention evaluation
  ///
  /// EXPECTED RESULT:
  /// Stream row is hard-deleted first, then plugin OnRemovedAsync is invoked.
  /// (Plugins are notified only after the row delete succeeds, so a failed delete
  /// does not leave plugins notified for a stream that still exists.)
  /// </summary>
  [Test]
  public async Task SoftDeletedStream_NoSegments_HardDeletedThenPluginNotified()
  {
    var streamId = Guid.NewGuid();
    var stream = MakeStream(RetentionMode.Default, 0, streamId);
    stream.DeletedAt = (ulong)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000);

    var data = new FakeDataProvider();
    var camera = MakeCamera(RetentionMode.Default, 0);
    data.AddCamera(camera);
    data.AddStream(stream);

    var plugin = new RecordingPluginStreamSettings();
    var storage = new FakeStorage();
    var host = MakeHost(data, storage, plugin);
    var engine = new RetentionEngine(host, new StubRecordingController(), NullLogger.Instance);

    await engine.EvaluateAsync(storage, CancellationToken.None);

    Assert.That(plugin.OnRemovedCalls, Has.Count.EqualTo(1));
    Assert.That(plugin.OnRemovedCalls[0], Is.EqualTo(streamId));
    Assert.That(((FakeStreamRepo)data.Streams).DeletedIds, Does.Contain(streamId));
    Assert.That(plugin.OnRemovedCalledBeforeDelete, Is.False,
      "OnRemovedAsync must be invoked after the stream row is hard-deleted");
  }

  /// <summary>
  /// SCENARIO:
  /// Soft-deleted stream still has at least one segment
  ///
  /// ACTION:
  /// Run retention evaluation
  ///
  /// EXPECTED RESULT:
  /// Stream row is NOT hard-deleted; plugin not notified yet
  /// </summary>
  [Test]
  public async Task SoftDeletedStream_HasSegments_NotDeleted()
  {
    var now = (ulong)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000);
    var day = 86_400_000_000UL;
    var streamId = Guid.NewGuid();
    var stream = MakeStream(RetentionMode.Default, 0, streamId);
    stream.DeletedAt = now;
    var seg = MakeSegment(streamId, now - day, now - day + 1000, size: 100);

    var data = new FakeDataProvider();
    var camera = MakeCamera(RetentionMode.Default, 0);
    data.AddCamera(camera);
    data.AddStream(stream);
    data.AddSegments(streamId, [seg]);

    var plugin = new RecordingPluginStreamSettings();
    var storage = new FakeStorage();
    var host = MakeHost(data, storage, plugin);
    var engine = new RetentionEngine(host, new StubRecordingController(), NullLogger.Instance);

    await engine.EvaluateAsync(storage, CancellationToken.None);

    Assert.That(((FakeStreamRepo)data.Streams).DeletedIds, Does.Not.Contain(streamId));
    Assert.That(plugin.OnRemovedCalls, Is.Empty);
  }

  /// <summary>
  /// SCENARIO:
  /// Storage holds 3 GB of recordings with 1 GB free; the 2 GB minimum free space leaves
  /// 2 GB usable. A Percent/50 stream holds three 0.6 GB one-hour segments
  ///
  /// ACTION:
  /// Run retention evaluation
  ///
  /// EXPECTED RESULT:
  /// The stream's quota is 1 GB (50% of usable), so the two oldest segments are purged
  /// </summary>
  [Test]
  public async Task PurgeByPercent_TrimsToShareOfUsableSpace()
  {
    var streamId = Guid.NewGuid();
    var now = Now();
    var size = (long)(0.6 * Gb);
    var seg1 = MakeSegment(streamId, now - 3 * Hour, now - 2 * Hour, size);
    var seg2 = MakeSegment(streamId, now - 2 * Hour, now - Hour, size);
    var seg3 = MakeSegment(streamId, now - Hour, now, size);

    var data = new FakeDataProvider();
    data.AddCamera(MakeCamera(RetentionMode.Default, 0));
    data.AddStream(MakeStream(RetentionMode.Percent, 50, streamId));
    data.AddSegments(streamId, [seg1, seg2, seg3]);

    var storage = new FakeStorage(totalBytes: 4 * Gb, usedBytes: 3 * Gb);
    await CreateEngine(data, storage).EvaluateAsync(storage, CancellationToken.None);

    Assert.That(data.DeletedSegmentIds, Is.EquivalentTo(new[] { seg1.Id, seg2.Id }));
  }

  /// <summary>
  /// SCENARIO:
  /// Storage cannot report its size; a Percent/50 stream holds old segments
  ///
  /// ACTION:
  /// Run retention evaluation
  ///
  /// EXPECTED RESULT:
  /// Nothing is purged; percent retention is unavailable without a known size
  /// </summary>
  [Test]
  public async Task PurgeByPercent_UnknownStorage_NotTrimmed()
  {
    var streamId = Guid.NewGuid();
    var now = Now();
    var seg1 = MakeSegment(streamId, now - 300 * Hour, now - 299 * Hour, size: Gb);

    var data = new FakeDataProvider();
    data.AddCamera(MakeCamera(RetentionMode.Default, 0));
    data.AddStream(MakeStream(RetentionMode.Percent, 50, streamId));
    data.AddSegments(streamId, [seg1]);

    var storage = new FakeStorage(totalBytes: -1, usedBytes: 0);
    await CreateEngine(data, storage).EvaluateAsync(storage, CancellationToken.None);

    Assert.That(data.DeletedSegmentIds, Is.Empty);
  }

  /// <summary>
  /// SCENARIO:
  /// Days/7 stream whose oldest segment is 10 days old but is the segment currently being
  /// written
  ///
  /// ACTION:
  /// Run retention evaluation
  ///
  /// EXPECTED RESULT:
  /// The segment being written is never purged
  /// </summary>
  [Test]
  public async Task ActiveSegment_NeverPurged()
  {
    var streamId = Guid.NewGuid();
    var now = Now();
    var seg = MakeSegment(streamId, now - 240 * Hour, now - 239 * Hour);

    var data = new FakeDataProvider();
    data.AddCamera(MakeCamera(RetentionMode.Default, 0));
    data.AddStream(MakeStream(RetentionMode.Days, 7, streamId));
    data.AddSegments(streamId, [seg]);

    var storage = new FakeStorage();
    var recording = new StubRecordingController { ActiveSegmentIds = new HashSet<Guid> { seg.Id } };
    var engine = new RetentionEngine(MakeHost(data, storage), recording, NullLogger.Instance);
    await engine.EvaluateAsync(storage, CancellationToken.None);

    Assert.That(data.DeletedSegmentIds, Is.Empty);
  }

  /// <summary>
  /// SCENARIO:
  /// A camera has no recorded segments at all
  ///
  /// ACTION:
  /// Run retention evaluation
  ///
  /// EXPECTED RESULT:
  /// Its events are purged on the system event retention (default 180 days)
  /// </summary>
  [Test]
  public async Task Events_CameraWithoutSegments_PurgedOnSystemEventRetention()
  {
    var data = new FakeDataProvider();
    var camera = MakeCamera(RetentionMode.Default, 0);
    data.AddCamera(camera);
    data.AddStream(MakeStream(RetentionMode.Default, 0));

    var storage = new FakeStorage();
    var before = DateTimeOffset.UtcNow.AddDays(-180).ToUnixMicroseconds();
    await CreateEngine(data, storage).EvaluateAsync(storage, CancellationToken.None);
    var after = DateTimeOffset.UtcNow.AddDays(-180).ToUnixMicroseconds();

    Assert.That(data.PurgedEvents, Has.Count.EqualTo(1));
    Assert.That(data.PurgedEvents[0].CameraId, Is.EqualTo(camera.Id));
    Assert.That(data.PurgedEvents[0].Cutoff, Is.InRange(before, after));
  }

  /// <summary>
  /// SCENARIO:
  /// Free space drops below the 0.2 GB hard floor while recording
  ///
  /// ACTION:
  /// CheckEmergencyAsync
  ///
  /// EXPECTED RESULT:
  /// All recording halts and an emergency stop event is logged
  /// </summary>
  [Test]
  public async Task Emergency_BelowHardFloor_HaltsRecording()
  {
    var data = new FakeDataProvider();
    var storage = new FakeStorage(totalBytes: Gb, usedBytes: Gb - (long)(0.1 * Gb));
    var recording = new StubRecordingController();
    var engine = new RetentionEngine(MakeHost(data, storage), recording, NullLogger.Instance);

    await engine.CheckEmergencyAsync(storage, CancellationToken.None);

    Assert.That(recording.IsHalted, Is.True);
    Assert.That(data.CreatedSystemEvents.Select(e => e.Type), Is.EqualTo(new[] { "retention-emergency-stop" }));
  }

  /// <summary>
  /// SCENARIO:
  /// Recording is halted and free space has recovered above the 2 GB minimum
  ///
  /// ACTION:
  /// CheckEmergencyAsync
  ///
  /// EXPECTED RESULT:
  /// Recording resumes and a resumed event is logged
  /// </summary>
  [Test]
  public async Task Emergency_RecoveredAboveMinimum_ResumesRecording()
  {
    var data = new FakeDataProvider();
    var storage = new FakeStorage(totalBytes: 10 * Gb, usedBytes: 5 * Gb);
    var recording = new StubRecordingController();
    await recording.HaltAllAsync();
    var engine = new RetentionEngine(MakeHost(data, storage), recording, NullLogger.Instance);

    await engine.CheckEmergencyAsync(storage, CancellationToken.None);

    Assert.That(recording.IsHalted, Is.False);
    Assert.That(data.CreatedSystemEvents.Select(e => e.Type), Is.EqualTo(new[] { "retention-recording-resumed" }));
  }

  /// <summary>
  /// SCENARIO:
  /// Storage cannot report free space
  ///
  /// ACTION:
  /// CheckEmergencyAsync
  ///
  /// EXPECTED RESULT:
  /// Recording is not halted; the emergency stop is unavailable
  /// </summary>
  [Test]
  public async Task Emergency_UnknownFreeSpace_DoesNothing()
  {
    var data = new FakeDataProvider();
    var storage = new FakeStorage(totalBytes: -1, usedBytes: 0);
    var recording = new StubRecordingController();
    var engine = new RetentionEngine(MakeHost(data, storage), recording, NullLogger.Instance);

    await engine.CheckEmergencyAsync(storage, CancellationToken.None);

    Assert.That(recording.IsHalted, Is.False);
    Assert.That(data.CreatedSystemEvents, Is.Empty);
  }

  /// <summary>
  /// SCENARIO:
  /// Storage cannot report free space and the emergency check runs every second
  ///
  /// ACTION:
  /// CheckEmergencyAsync three times
  ///
  /// EXPECTED RESULT:
  /// The unavailable-safeguards warning is logged once, not on every check
  /// </summary>
  [Test]
  public async Task Emergency_UnknownFreeSpace_WarnsOnce()
  {
    var storage = new FakeStorage(totalBytes: -1, usedBytes: 0);
    var logger = new ListLogger();
    var engine = new RetentionEngine(MakeHost(new FakeDataProvider(), storage), new StubRecordingController(), logger);

    for (var i = 0; i < 3; i++)
      await engine.CheckEmergencyAsync(storage, CancellationToken.None);

    Assert.That(logger.Entries.Count(e => e.Level == LogLevel.Warning), Is.EqualTo(1));
  }

  private sealed class ListLogger : ILogger
  {
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
      Func<TState, Exception?, string> formatter) =>
      Entries.Add((logLevel, formatter(state, exception)));
  }

  /// <summary>
  /// SCENARIO:
  /// Free space is below the 2 GB minimum; a Days/7 stream holds a 10-day-old segment
  ///
  /// ACTION:
  /// CheckFreeSpaceAsync, then wait for the dispatched pass
  ///
  /// EXPECTED RESULT:
  /// A retention pass runs early, purges the old segment, and logs a low space warning
  /// </summary>
  [Test]
  public async Task FreeSpaceCheck_BelowMinimum_RunsPassAndWarns()
  {
    var streamId = Guid.NewGuid();
    var now = Now();
    var old = MakeSegment(streamId, now - 240 * Hour, now - 239 * Hour);

    var data = new FakeDataProvider();
    data.AddCamera(MakeCamera(RetentionMode.Default, 0));
    data.AddStream(MakeStream(RetentionMode.Days, 7, streamId));
    data.AddSegments(streamId, [old]);

    var storage = new FakeStorage(totalBytes: 100 * Gb, usedBytes: 99 * Gb);
    var engine = new RetentionEngine(MakeHost(data, storage), new StubRecordingController(), NullLogger.Instance);

    await engine.CheckFreeSpaceAsync(storage, CancellationToken.None);
    await engine.Pass;

    Assert.That(data.DeletedSegmentIds, Is.EqualTo(new[] { old.Id }));
    Assert.That(data.CreatedSystemEvents.Select(e => e.Type), Is.EqualTo(new[] { "retention-low-space-purge" }));
  }

  /// <summary>
  /// SCENARIO:
  /// Free space is below the minimum and a retention pass is still running when the next
  /// free space check fires
  ///
  /// ACTION:
  /// CheckFreeSpaceAsync twice while the first pass is held, then release it
  ///
  /// EXPECTED RESULT:
  /// The second check does not start another pass; only one low space warning is logged
  /// </summary>
  [Test]
  public async Task FreeSpaceCheck_PassRunning_DoesNotOverlap()
  {
    var data = new FakeDataProvider();
    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var storage = new FakeStorage(totalBytes: 100 * Gb, usedBytes: 99 * Gb) { StatsGate = gate };
    var engine = new RetentionEngine(MakeHost(data, storage), new StubRecordingController(), NullLogger.Instance);

    await engine.CheckFreeSpaceAsync(storage, CancellationToken.None);
    var first = engine.Pass;
    await engine.CheckFreeSpaceAsync(storage, CancellationToken.None);

    Assert.That(engine.Pass, Is.SameAs(first));

    gate.SetResult();
    await engine.Pass;
    Assert.That(data.CreatedSystemEvents.Select(e => e.Type), Is.EqualTo(new[] { "retention-low-space-purge" }));
  }

  /// <summary>
  /// SCENARIO:
  /// Free space is above the 2 GB minimum
  ///
  /// ACTION:
  /// CheckFreeSpaceAsync
  ///
  /// EXPECTED RESULT:
  /// No pass is dispatched and no warning is logged
  /// </summary>
  [Test]
  public async Task FreeSpaceCheck_AboveMinimum_DoesNothing()
  {
    var data = new FakeDataProvider();
    var storage = new FakeStorage();
    var engine = new RetentionEngine(MakeHost(data, storage), new StubRecordingController(), NullLogger.Instance);

    await engine.CheckFreeSpaceAsync(storage, CancellationToken.None);
    await engine.Pass;

    Assert.That(data.CreatedSystemEvents, Is.Empty);
  }

  /// <summary>
  /// SCENARIO:
  /// Quality stream has RecordingEnabled = false and Days/7; one segment is 10 days old
  ///
  /// ACTION:
  /// Run retention evaluation
  ///
  /// EXPECTED RESULT:
  /// The old segment is purged; disabling recording does not exempt existing footage
  /// </summary>
  [Test]
  public async Task RecordingDisabledStream_StillPurgedByPolicy()
  {
    var now = (ulong)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000);
    var day = 86_400_000_000UL;
    var streamId = Guid.NewGuid();
    var seg10 = MakeSegment(streamId, now - 10 * day, now - 10 * day + 1000);

    var data = new FakeDataProvider();
    var stream = MakeStream(RetentionMode.Days, 7, streamId);
    stream.RecordingEnabled = false;
    data.AddCamera(MakeCamera(RetentionMode.Default, 0));
    data.AddStream(stream);
    data.AddSegments(streamId, [seg10]);

    var storage = new FakeStorage();
    await CreateEngine(data, storage).EvaluateAsync(storage, CancellationToken.None);

    Assert.That(data.DeletedSegmentIds, Is.EqualTo(new[] { seg10.Id }));
  }

  /// <summary>
  /// SCENARIO:
  /// Soft-deleted quality stream has Days/7; one segment is 10 days old, one is 1 day old
  ///
  /// ACTION:
  /// Run retention evaluation
  ///
  /// EXPECTED RESULT:
  /// The old segment is purged under the stream's policy; the recent one remains
  /// </summary>
  [Test]
  public async Task SoftDeletedStream_StillPurgedByPolicy()
  {
    var now = (ulong)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000);
    var day = 86_400_000_000UL;
    var streamId = Guid.NewGuid();
    var seg10 = MakeSegment(streamId, now - 10 * day, now - 10 * day + 1000);
    var seg1 = MakeSegment(streamId, now - 1 * day, now - 1 * day + 1000);

    var data = new FakeDataProvider();
    var stream = MakeStream(RetentionMode.Days, 7, streamId);
    stream.DeletedAt = now;
    data.AddCamera(MakeCamera(RetentionMode.Default, 0));
    data.AddStream(stream);
    data.AddSegments(streamId, [seg10, seg1]);

    var storage = new FakeStorage();
    await CreateEngine(data, storage).EvaluateAsync(storage, CancellationToken.None);

    Assert.That(data.DeletedSegmentIds, Is.EqualTo(new[] { seg10.Id }));
  }

  /// <summary>
  /// SCENARIO:
  /// Quality stream has Bytes/102 with three 50-byte segments; an attached metadata stream
  /// has 1-byte segments covering each quality segment, counted toward the same quota
  ///
  /// ACTION:
  /// Run retention evaluation
  ///
  /// EXPECTED RESULT:
  /// The oldest quality segment is purged, and the metadata segment that only covered it
  /// is purged with it; metadata covering the remaining quality segments is kept
  /// </summary>
  [Test]
  public async Task MetadataStream_PurgedWhereQualityNoLongerCovers()
  {
    var qualityId = Guid.NewGuid();
    var metadataId = Guid.NewGuid();
    var now = Now();
    var q1 = MakeSegment(qualityId, now - 3 * Hour, now - 2 * Hour, size: 50);
    var q2 = MakeSegment(qualityId, now - 2 * Hour, now - Hour, size: 50);
    var q3 = MakeSegment(qualityId, now - Hour, now, size: 50);
    var m1 = MakeSegment(metadataId, now - 3 * Hour, now - 2 * Hour, size: 1);
    var m2 = MakeSegment(metadataId, now - 2 * Hour, now - Hour, size: 1);
    var m3 = MakeSegment(metadataId, now - Hour, now, size: 1);

    var data = new FakeDataProvider();
    data.AddCamera(MakeCamera(RetentionMode.Default, 0));
    data.AddStream(MakeStream(RetentionMode.Bytes, 102, qualityId));
    data.AddStream(MakeMetadataStream(metadataId, qualityId));
    data.AddSegments(qualityId, [q1, q2, q3]);
    data.AddSegments(metadataId, [m1, m2, m3]);

    var storage = new FakeStorage();
    await CreateEngine(data, storage).EvaluateAsync(storage, CancellationToken.None);

    Assert.That(data.DeletedSegmentIds, Is.EquivalentTo(new[] { q1.Id, m1.Id }));
  }

  /// <summary>
  /// SCENARIO:
  /// Quality stream has no segments left; its attached metadata stream still has segments
  ///
  /// ACTION:
  /// Run retention evaluation
  ///
  /// EXPECTED RESULT:
  /// All metadata segments are purged
  /// </summary>
  [Test]
  public async Task MetadataStream_PurgedEntirelyWhenQualityHasNoSegments()
  {
    var qualityId = Guid.NewGuid();
    var metadataId = Guid.NewGuid();
    var m1 = MakeSegment(metadataId, 1_000_000, 2_000_000, size: 1);
    var m2 = MakeSegment(metadataId, 3_000_000, 4_000_000, size: 1);

    var data = new FakeDataProvider();
    data.AddCamera(MakeCamera(RetentionMode.Default, 0));
    data.AddStream(MakeStream(RetentionMode.Days, 7, qualityId));
    data.AddStream(MakeMetadataStream(metadataId, qualityId));
    data.AddSegments(metadataId, [m1, m2]);

    var storage = new FakeStorage();
    await CreateEngine(data, storage).EvaluateAsync(storage, CancellationToken.None);

    Assert.That(data.DeletedSegmentIds, Is.EquivalentTo(new[] { m1.Id, m2.Id }));
  }

  /// <summary>
  /// SCENARIO:
  /// Quality stream has Bytes/100 with three 50-byte segments; an attached metadata stream
  /// has a segment as old as the oldest quality segment
  ///
  /// ACTION:
  /// Run retention evaluation
  ///
  /// EXPECTED RESULT:
  /// Camera events are purged up to the oldest remaining quality segment, not held back
  /// by metadata that only covered purged footage
  /// </summary>
  [Test]
  public async Task Events_PurgedUpToOldestRemainingFootage()
  {
    var qualityId = Guid.NewGuid();
    var metadataId = Guid.NewGuid();
    var now = Now();
    var q1 = MakeSegment(qualityId, now - 3 * Hour, now - 2 * Hour, size: 50);
    var q2 = MakeSegment(qualityId, now - 2 * Hour, now - Hour, size: 50);
    var q3 = MakeSegment(qualityId, now - Hour, now, size: 50);
    var m1 = MakeSegment(metadataId, now - 3 * Hour, now - 2 * Hour, size: 1);

    var data = new FakeDataProvider();
    var camera = MakeCamera(RetentionMode.Default, 0);
    data.AddCamera(camera);
    data.AddStream(MakeStream(RetentionMode.Bytes, 100, qualityId));
    data.AddStream(MakeMetadataStream(metadataId, qualityId));
    data.AddSegments(qualityId, [q1, q2, q3]);
    data.AddSegments(metadataId, [m1]);

    var storage = new FakeStorage();
    await CreateEngine(data, storage).EvaluateAsync(storage, CancellationToken.None);

    Assert.That(data.PurgedEvents, Is.EqualTo(new[] { (camera.Id, q2.StartTime) }));
  }

  private static ulong Now() => DateTimeOffset.UtcNow.ToUnixMicroseconds();

  private static CameraStream MakeMetadataStream(Guid streamId, Guid parentStreamId) => new()
  {
    Id = streamId,
    CameraId = Guid.NewGuid(),
    Profile = "main-motion-grid",
    Kind = StreamKind.Metadata,
    FormatId = "mgrd",
    Codec = "mgrd",
    Uri = "",
    ParentStreamId = parentStreamId
  };

  private static RetentionEngine CreateEngine(FakeDataProvider data, FakeStorage storage)
  {
    var host = MakeHost(data, storage);
    return new RetentionEngine(host, new StubRecordingController(), NullLogger.Instance);
  }

  private sealed class StubRecordingController : IRecordingController
  {
    public bool IsHalted { get; private set; }
    public int WriterCount => 0;
    public IReadOnlySet<Guid> ActiveSegmentIds { get; set; } = new HashSet<Guid>();
    public Task HaltAllAsync() { IsHalted = true; return Task.CompletedTask; }
    public Task ResumeAsync(CancellationToken ct) { IsHalted = false; return Task.CompletedTask; }
  }

  private static FakePluginHost MakeHost(FakeDataProvider data, IStorageProvider storage, IPlugin? plugin = null)
  {
    if (plugin is RecordingPluginStreamSettings rp)
      rp.StreamExists = id => ((FakeStreamRepo)data.Streams).DeletedIds.Contains(id) == false;

    return new FakePluginHost
    {
      DataProvider = data,
      StorageProviders = [storage],
      Plugins = plugin != null
        ? [new PluginEntry
          {
            PluginType = plugin.GetType(),
            LoadContext = AssemblyLoadContext.Default,
            Plugin = plugin,
            Metadata = plugin.Metadata
          }]
        : []
    };
  }

  private static CameraStream MakeStream(
    RetentionMode mode, long value, Guid? streamId = null) => new()
  {
    Id = streamId ?? Guid.NewGuid(),
    CameraId = Guid.NewGuid(),
    Profile = "main",
    Kind = StreamKind.Quality,
    FormatId = "fmp4",
    Codec = "h264",
    Uri = "rtsp://test",
    RecordingEnabled = true,
    RetentionMode = mode,
    RetentionValue = value
  };

  private static Camera MakeCamera(RetentionMode mode, long value) => new()
  {
    Id = Guid.NewGuid(),
    Name = "Test",
    Address = "192.168.1.1",
    ProviderId = "test",
    RetentionMode = mode,
    RetentionValue = value
  };

  private static Segment MakeSegment(
    Guid streamId, ulong start, ulong end, long size = 1000) => new()
  {
    Id = Guid.NewGuid(),
    StreamId = streamId,
    StartTime = start,
    EndTime = end,
    SegmentRef = $"ref/{start}",
    SizeBytes = size,
    KeyframeCount = 1
  };

  private sealed class FakeStorage : IStorageProvider
  {
    private readonly long _totalBytes;
    private readonly long _usedBytes;

    public string ProviderId => "fake";
    public List<string> PurgedRefs { get; } = [];

    public FakeStorage(long totalBytes = 1_000 * Gb, long usedBytes = 500 * Gb)
    {
      _totalBytes = totalBytes;
      _usedBytes = usedBytes;
    }

    public Task<OneOf<ISegmentHandle, Error>> CreateSegmentAsync(SegmentMetadata metadata, CancellationToken ct) =>
      throw new NotImplementedException();

    public Task<OneOf<Stream, Error>> OpenReadAsync(string segmentRef, CancellationToken ct) =>
      throw new NotImplementedException();

    public Task<OneOf<Success, Error>> PurgeAsync(IReadOnlyList<string> segmentRefs, CancellationToken ct)
    {
      PurgedRefs.AddRange(segmentRefs);
      return Task.FromResult<OneOf<Success, Error>>(new Success());
    }

    public TaskCompletionSource? StatsGate { get; init; }

    public async Task<OneOf<StorageStats, Error>> GetStatsAsync(CancellationToken ct)
    {
      if (StatsGate != null)
        await StatsGate.Task;

      return new StorageStats
      {
        TotalBytes = _totalBytes,
        UsedBytes = _usedBytes,
        FreeBytes = _totalBytes - _usedBytes,
        RecordingBytes = _usedBytes
      };
    }

    public Task<OneOf<long, Error>> GetFreeBytesAsync(CancellationToken ct) =>
      Task.FromResult<OneOf<long, Error>>(_totalBytes - _usedBytes);
  }

  private sealed class FakeDataProvider : IDataProvider
  {
    private readonly List<Camera> _cameras = [];
    private readonly Dictionary<Guid, List<CameraStream>> _streams = [];
    private readonly Dictionary<Guid, List<Segment>> _segments = [];

    public string ProviderId => "fake";
    public ICameraRepository Cameras { get; }
    public IStreamRepository Streams { get; }
    public ISegmentRepository Segments { get; }
    public IKeyframeRepository Keyframes { get; }
    public IEventRepository Events { get; }
    public ISystemEventRepository SystemEvents { get; } = new FakeSystemEventRepo();
    public IClientRepository Clients => throw new NotImplementedException();
    public IConfigRepository Config { get; }
    public IDataStore GetDataStore(string pluginId) => throw new NotImplementedException();

    public List<Guid> DeletedSegmentIds => ((FakeSegmentRepo)Segments).DeletedIds;
    public List<Guid> DeletedKeyframeSegmentIds => ((FakeKeyframeRepo)Keyframes).DeletedSegmentIds;

    public FakeDataProvider()
    {
      Cameras = new FakeCameraRepo(_cameras);
      Streams = new FakeStreamRepo(_streams);
      Segments = new FakeSegmentRepo(_segments);
      Keyframes = new FakeKeyframeRepo();
      Events = new FakeEventRepo();
      Config = new FakeConfigRepo();
    }

    public List<(Guid CameraId, ulong Cutoff)> PurgedEvents => ((FakeEventRepo)Events).Purged;
    public List<SystemEvent> CreatedSystemEvents => ((FakeSystemEventRepo)SystemEvents).Created;

    public void AddCamera(Camera camera) => _cameras.Add(camera);

    public void AddStream(CameraStream stream)
    {
      stream.CameraId = _cameras.Last().Id;
      if (!_streams.ContainsKey(stream.CameraId))
        _streams[stream.CameraId] = [];
      _streams[stream.CameraId].Add(stream);
    }

    public void AddSegments(Guid streamId, List<Segment> segments)
    {
      _segments[streamId] = segments;
    }
  }

  private sealed class FakeCameraRepo : ICameraRepository
  {
    private readonly List<Camera> _cameras;
    public FakeCameraRepo(List<Camera> cameras) => _cameras = cameras;

    public Task<OneOf<IReadOnlyList<Camera>, Error>> GetAllAsync(CancellationToken ct) =>
      Task.FromResult<OneOf<IReadOnlyList<Camera>, Error>>(_cameras.ToList());

    public Task<OneOf<Camera, Error>> GetByIdAsync(Guid id, CancellationToken ct) =>
      throw new NotImplementedException();
    public Task<OneOf<Camera, Error>> GetByAddressAsync(string address, CancellationToken ct) =>
      throw new NotImplementedException();
    public Task<OneOf<Success, Error>> CreateAsync(Camera camera, CancellationToken ct) =>
      throw new NotImplementedException();
    public Task<OneOf<Success, Error>> UpdateAsync(Camera camera, CancellationToken ct) =>
      throw new NotImplementedException();
    public Task<OneOf<Success, Error>> DeleteAsync(Guid id, CancellationToken ct) =>
      throw new NotImplementedException();
  }

  private sealed class FakeStreamRepo : IStreamRepository
  {
    private readonly Dictionary<Guid, List<CameraStream>> _streams;
    public FakeStreamRepo(Dictionary<Guid, List<CameraStream>> streams) => _streams = streams;

    public Task<OneOf<IReadOnlyList<CameraStream>, Error>> GetByCameraIdAsync(
      Guid cameraId, CancellationToken ct)
    {
      var list = _streams.GetValueOrDefault(cameraId) ?? [];
      return Task.FromResult<OneOf<IReadOnlyList<CameraStream>, Error>>(list.ToList());
    }

    public List<Guid> DeletedIds { get; } = [];

    public Task<OneOf<CameraStream, Error>> GetByIdAsync(Guid id, CancellationToken ct) =>
      throw new NotImplementedException();
    public Task<OneOf<Success, Error>> UpsertAsync(CameraStream stream, CancellationToken ct) =>
      throw new NotImplementedException();
    public Task<OneOf<Success, Error>> DeleteAsync(Guid id, CancellationToken ct)
    {
      DeletedIds.Add(id);
      foreach (var list in _streams.Values)
        list.RemoveAll(s => s.Id == id);
      return Task.FromResult<OneOf<Success, Error>>(new Success());
    }
  }

  private sealed class FakeSegmentRepo : ISegmentRepository
  {
    private readonly Dictionary<Guid, List<Segment>> _segments;
    public List<Guid> DeletedIds { get; } = [];

    public FakeSegmentRepo(Dictionary<Guid, List<Segment>> segments) => _segments = segments;

    public Task<OneOf<Segment, Error>> GetByIdAsync(Guid id, CancellationToken ct) =>
      throw new NotImplementedException();

    public Task<OneOf<PlaybackPoint, Error>> FindPlaybackPointAsync(
      Guid streamId, ulong timestamp, CancellationToken ct) =>
      throw new NotImplementedException();

    public Task<OneOf<IReadOnlyList<Segment>, Error>> GetByTimeRangeAsync(
      Guid streamId, ulong from, ulong to, CancellationToken ct)
    {
      var list = _segments.GetValueOrDefault(streamId) ?? [];
      var filtered = list.Where(s => s.StartTime <= to && s.EndTime >= from).ToList();
      return Task.FromResult<OneOf<IReadOnlyList<Segment>, Error>>(filtered);
    }

    public Task<OneOf<IReadOnlyList<Segment>, Error>> GetOldestAsync(
      Guid streamId, int limit, CancellationToken ct)
    {
      var list = _segments.GetValueOrDefault(streamId) ?? [];
      var ordered = list.OrderBy(s => s.StartTime).Take(limit).ToList();
      return Task.FromResult<OneOf<IReadOnlyList<Segment>, Error>>(ordered);
    }

    public Task<OneOf<IReadOnlyList<Segment>, Error>> GetOldestAcrossStreamsAsync(
      int limit, CancellationToken ct)
    {
      var ordered = _segments.Values.SelectMany(v => v).OrderBy(s => s.StartTime).Take(limit).ToList();
      return Task.FromResult<OneOf<IReadOnlyList<Segment>, Error>>(ordered);
    }

    public Task<OneOf<long, Error>> GetTotalSizeAsync(Guid streamId, CancellationToken ct)
    {
      var list = _segments.GetValueOrDefault(streamId) ?? [];
      return Task.FromResult<OneOf<long, Error>>(list.Sum(s => s.SizeBytes));
    }

    public Task<OneOf<Success, Error>> CreateAsync(Segment segment, CancellationToken ct) =>
      Task.FromResult<OneOf<Success, Error>>(new Success());

    public Task<OneOf<Success, Error>> UpdateAsync(Segment segment, CancellationToken ct) =>
      Task.FromResult<OneOf<Success, Error>>(new Success());

    public Task<OneOf<Success, Error>> DeleteBatchAsync(IReadOnlyList<Guid> ids, CancellationToken ct)
    {
      DeletedIds.AddRange(ids);
      foreach (var list in _segments.Values)
        list.RemoveAll(s => ids.Contains(s.Id));
      return Task.FromResult<OneOf<Success, Error>>(new Success());
    }
    public Task<OneOf<IReadOnlyList<StreamStorageUsage>, Error>> GetSizeBreakdownAsync(CancellationToken ct) =>
      throw new NotImplementedException();
  }

  private sealed class FakeSystemEventRepo : ISystemEventRepository
  {
    public List<SystemEvent> Created { get; } = [];
    public List<ulong> Purged { get; } = [];

    public Task<OneOf<IReadOnlyList<SystemEvent>, Error>> QueryAsync(
      string? type, ulong from, ulong to, int limit, int offset, CancellationToken ct = default) =>
      Task.FromResult<OneOf<IReadOnlyList<SystemEvent>, Error>>(Array.Empty<SystemEvent>());

    public Task<OneOf<SystemEvent, Error>> GetByIdAsync(Guid id, CancellationToken ct = default) =>
      Task.FromResult<OneOf<SystemEvent, Error>>(
        Error.Create(0, 0, Result.NotFound, $"System event {id} not found"));

    public Task<OneOf<Success, Error>> CreateAsync(SystemEvent evt, CancellationToken ct = default)
    {
      Created.Add(evt);
      return Task.FromResult<OneOf<Success, Error>>(new Success());
    }

    public Task<OneOf<int, Error>> DeleteOlderThanAsync(ulong cutoff, CancellationToken ct = default)
    {
      Purged.Add(cutoff);
      return Task.FromResult<OneOf<int, Error>>(0);
    }
  }

  private sealed class FakeEventRepo : IEventRepository
  {
    public List<(Guid CameraId, ulong Cutoff)> Purged { get; } = [];

    public Task<OneOf<int, Error>> DeleteOlderThanAsync(Guid cameraId, ulong cutoff, CancellationToken ct)
    {
      Purged.Add((cameraId, cutoff));
      return Task.FromResult<OneOf<int, Error>>(0);
    }

    public Task<OneOf<IReadOnlyList<CameraEvent>, Error>> QueryAsync(
      Guid? cameraId, string? type, ulong from, ulong to, int limit, int offset, CancellationToken ct) =>
      throw new NotImplementedException();
    public Task<OneOf<CameraEvent, Error>> GetByIdAsync(Guid id, CancellationToken ct) =>
      throw new NotImplementedException();
    public Task<OneOf<Success, Error>> CreateAsync(CameraEvent evt, CancellationToken ct) =>
      throw new NotImplementedException();
    public Task<OneOf<Success, Error>> UpdateAsync(CameraEvent evt, CancellationToken ct) =>
      throw new NotImplementedException();
    public Task<OneOf<IReadOnlyList<CameraEvent>, Error>> GetByTimeRangeAsync(
      Guid cameraId, ulong from, ulong to, CancellationToken ct) =>
      throw new NotImplementedException();
  }

  private sealed class FakeKeyframeRepo : IKeyframeRepository
  {
    public List<Guid> DeletedSegmentIds { get; } = [];

    public Task<OneOf<Success, Error>> CreateBatchAsync(
      IReadOnlyList<Keyframe> keyframes, CancellationToken ct) =>
      Task.FromResult<OneOf<Success, Error>>(new Success());

    public Task<OneOf<Success, Error>> DeleteBySegmentIdsAsync(
      IReadOnlyList<Guid> segmentIds, CancellationToken ct)
    {
      DeletedSegmentIds.AddRange(segmentIds);
      return Task.FromResult<OneOf<Success, Error>>(new Success());
    }

    public Task<OneOf<IReadOnlyList<Keyframe>, Error>> GetBySegmentIdAsync(
      Guid segmentId, CancellationToken ct) =>
      throw new NotImplementedException();

    public Task<OneOf<Keyframe, Error>> GetNearestAsync(
      Guid segmentId, ulong timestamp, CancellationToken ct) =>
      throw new NotImplementedException();
  }

  private sealed class FakeConfigRepo : IConfigRepository
  {
    public Task<OneOf<string?, Error>> GetAsync(string pluginId, string key, CancellationToken ct) =>
      Task.FromResult<OneOf<string?, Error>>((string?)null);

    public Task<OneOf<IReadOnlyDictionary<string, string>, Error>> GetAllAsync(
      string pluginId, CancellationToken ct) =>
      Task.FromResult<OneOf<IReadOnlyDictionary<string, string>, Error>>(
        new Dictionary<string, string>());

    public Task<OneOf<Success, Error>> SetAsync(
      string pluginId, string key, string value, CancellationToken ct) =>
      Task.FromResult<OneOf<Success, Error>>(new Success());

    public Task<OneOf<Success, Error>> DeleteAsync(
      string pluginId, string key, CancellationToken ct) =>
      Task.FromResult<OneOf<Success, Error>>(new Success());
  }

  private sealed class RecordingPluginStreamSettings : IPlugin, IPluginStreamSettings
  {
    public List<Guid> OnRemovedCalls { get; } = [];
    public bool OnRemovedCalledBeforeDelete { get; private set; }
    public Func<Guid, bool>? StreamExists { get; set; }

    public PluginMetadata Metadata { get; } = new()
    {
      Id = "test-stream-settings",
      Name = "Test",
      Version = "1.0.0",
      Description = ""
    };
    public OneOf<Success, Error> Initialize(PluginContext context) => new Success();
    public Task<OneOf<Success, Error>> StartAsync(CancellationToken ct) =>
      Task.FromResult<OneOf<Success, Error>>(new Success());
    public Task<OneOf<Success, Error>> StopAsync(CancellationToken ct) =>
      Task.FromResult<OneOf<Success, Error>>(new Success());

    public IReadOnlyList<SettingGroup> GetSchema(Guid streamId) => [];
    public IReadOnlyDictionary<string, string> GetValues(Guid streamId) =>
      new Dictionary<string, string>();
    public OneOf<Success, Error> ValidateValue(Guid streamId, string key, string value) =>
      new Success();
    public OneOf<Success, Error> ApplyValues(Guid streamId, IReadOnlyDictionary<string, string> values) =>
      new Success();

    public Task<OneOf<Success, Error>> OnRemovedAsync(Guid streamId, CancellationToken ct)
    {
      OnRemovedCalls.Add(streamId);
      OnRemovedCalledBeforeDelete = StreamExists?.Invoke(streamId) ?? true;
      return Task.FromResult<OneOf<Success, Error>>(new Success());
    }
  }

}
