using System.Threading.Channels;
using Server.Streaming;
using Shared.Models.Formats;

namespace Tests.Unit.Streaming;

[TestFixture]
public class MuxStreamFanOutTests
{
  private static MuxStreamInfo TestInfo => new()
  {
    DataFormat = "fmp4",
    MimeType = "video/mp4; codecs=\"avc1.640029\"",
    FileExtension = "mp4",
    Resolution = "1920x1080",
    Fps = 30
  };

  private static Fmp4Fragment MakeFragment(ulong ts, bool sync = false, bool header = false) => new()
  {
    Data = new byte[] { 0x00, 0x00, 0x00, 0x08, 0x6d, 0x6f, 0x6f, 0x66 },
    Timestamp = ts,
    MediaTimestamp = 0,
    IsSyncPoint = sync,
    IsHeader = header
  };

  /// <summary>
  /// SCENARIO:
  /// Subscribers are added and one leaves
  ///
  /// ACTION:
  /// Subscribe twice, first subscriber's ReadAsync completes
  ///
  /// EXPECTED RESULT:
  /// GetDemand reflects the live subscribers: 0 -> 1 -> 2 -> 1
  /// </summary>
  [Test]
  public async Task GetDemand_TracksLiveSubscribers()
  {
    var source = new TestMuxStream(TestInfo);
    await using var fanOut = new MuxStreamFanOut<Fmp4Fragment>(source);

    Assert.That(fanOut.GetDemand(), Is.EqualTo(0));

    var sub1 = fanOut.Subscribe();
    Assert.That(fanOut.GetDemand(), Is.EqualTo(1));

    var sub2 = fanOut.Subscribe();
    Assert.That(fanOut.GetDemand(), Is.EqualTo(2));

    using var cts = new CancellationTokenSource(50);
    await foreach (var _ in ((IMuxStream<Fmp4Fragment>)sub1).ReadAsync(cts.Token)) { }

    Assert.That(fanOut.GetDemand(), Is.EqualTo(1));
    Assert.That(fanOut.SubscriberCount, Is.EqualTo(1));
  }

  /// <summary>
  /// SCENARIO:
  /// Changed callback is set on the fan-out
  ///
  /// ACTION:
  /// Subscribe twice, then one subscriber leaves
  ///
  /// EXPECTED RESULT:
  /// Changed fires on every subscribe and unsubscribe
  /// </summary>
  [Test]
  public async Task Changed_FiresOnEverySubscriptionChange()
  {
    var source = new TestMuxStream(TestInfo);
    await using var fanOut = new MuxStreamFanOut<Fmp4Fragment>(source);

    var changedCount = 0;
    fanOut.Changed = () => changedCount++;

    var sub1 = fanOut.Subscribe();
    Assert.That(changedCount, Is.EqualTo(1));

    fanOut.Subscribe();
    Assert.That(changedCount, Is.EqualTo(2));

    using var cts = new CancellationTokenSource(50);
    await foreach (var _ in ((IMuxStream<Fmp4Fragment>)sub1).ReadAsync(cts.Token)) { }

    Assert.That(changedCount, Is.EqualTo(3));
  }

  /// <summary>
  /// SCENARIO:
  /// New subscriber joins while stream is active
  ///
  /// ACTION:
  /// Subscribe after data is flowing
  ///
  /// EXPECTED RESULT:
  /// Subscriber skips non-keyframes until the first keyframe arrives
  /// </summary>
  [Test]
  public async Task NewSubscriber_WaitsForKeyframe()
  {
    var source = new TestMuxStream(TestInfo);
    await using var fanOut = new MuxStreamFanOut<Fmp4Fragment>(source);

    var sub = fanOut.Subscribe();

    source.Emit(MakeFragment(1, sync: false));
    source.Emit(MakeFragment(2, sync: false));
    source.Emit(MakeFragment(3, sync: true));
    source.Emit(MakeFragment(4, sync: false));

    await Task.Delay(100);

    var received = new List<ulong>();
    using var cts = new CancellationTokenSource(100);
    await foreach (var item in ((IMuxStream<Fmp4Fragment>)sub).ReadAsync(cts.Token))
      received.Add(item.Timestamp);

    Assert.That(received, Does.Contain(3UL));
    Assert.That(received, Does.Not.Contain(1UL));
    Assert.That(received, Does.Not.Contain(2UL));
  }

  /// <summary>
  /// SCENARIO:
  /// A caller subscribes but returns before it ever starts reading the stream
  ///
  /// ACTION:
  /// Dispose the subscription without enumerating it
  ///
  /// EXPECTED RESULT:
  /// The subscriber is removed, so no demand is left raised on the pipeline
  /// </summary>
  [Test]
  public async Task Dispose_WithoutEnumerating_RemovesSubscriber()
  {
    var source = new TestMuxStream(TestInfo);
    await using var fanOut = new MuxStreamFanOut<Fmp4Fragment>(source);

    var sub = fanOut.Subscribe();
    Assert.That(fanOut.GetDemand(), Is.EqualTo(1));

    ((IDisposable)sub).Dispose();

    Assert.That(fanOut.GetDemand(), Is.EqualTo(0));
    Assert.That(fanOut.SubscriberCount, Is.EqualTo(0));
  }

  /// <summary>
  /// SCENARIO:
  /// A subscription is disposed explicitly and then its read also ends
  ///
  /// ACTION:
  /// Dispose the subscription, then enumerate it to completion
  ///
  /// EXPECTED RESULT:
  /// The unsubscribe runs exactly once: Changed fires once for the subscribe and once for the
  /// unsubscribe
  /// </summary>
  [Test]
  public async Task Dispose_ThenReadEnds_UnsubscribesOnce()
  {
    var source = new TestMuxStream(TestInfo);
    await using var fanOut = new MuxStreamFanOut<Fmp4Fragment>(source);

    var changedCount = 0;
    fanOut.Changed = () => changedCount++;

    var sub = fanOut.Subscribe();
    ((IDisposable)sub).Dispose();
    await foreach (var _ in sub.ReadAsync(CancellationToken.None)) { }
    ((IDisposable)sub).Dispose();

    Assert.That(changedCount, Is.EqualTo(2));
  }

  /// <summary>
  /// SCENARIO:
  /// A reader is waiting on a subscription that has no data and no cancellation
  ///
  /// ACTION:
  /// Dispose the subscription from another thread
  ///
  /// EXPECTED RESULT:
  /// The waiting read completes instead of hanging on a channel nobody will write to again
  /// </summary>
  [Test]
  public async Task Dispose_EndsWaitingReader()
  {
    var source = new TestMuxStream(TestInfo);
    await using var fanOut = new MuxStreamFanOut<Fmp4Fragment>(source);

    var sub = fanOut.Subscribe();
    var reader = Task.Run(async () =>
    {
      await foreach (var _ in sub.ReadAsync(CancellationToken.None)) { }
    });

    await Task.Delay(50);
    ((IDisposable)sub).Dispose();

    await reader.WaitAsync(TimeSpan.FromSeconds(2));
    Assert.That(reader.IsCompletedSuccessfully, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// The current GOP has grown far past what a subscriber's channel can hold
  ///
  /// ACTION:
  /// Subscribe mid-GOP, then let the stream continue through the next keyframe
  ///
  /// EXPECTED RESULT:
  /// The subscriber is not handed a replay with its keyframe missing: it receives nothing
  /// until the next keyframe and everything from there on
  /// </summary>
  [Test]
  public async Task OversizedGop_LateSubscriberWaitsForNextKeyframe()
  {
    var source = new TestMuxStream(TestInfo);
    await using var fanOut = new MuxStreamFanOut<Fmp4Fragment>(source);
    var first = fanOut.Subscribe();

    source.Emit(MakeFragment(1, sync: true));
    for (ulong ts = 2; ts < 2 + 2 * (ulong)MuxStreamFanOut<Fmp4Fragment>.MaxCachedFragments; ts++)
      source.Emit(MakeFragment(ts));
    await Task.Delay(100);

    var late = fanOut.Subscribe();
    source.Emit(MakeFragment(9000));
    source.Emit(MakeFragment(9001, sync: true));
    source.Emit(MakeFragment(9002));
    await Task.Delay(100);

    var received = new List<ulong>();
    using var cts = new CancellationTokenSource(100);
    await foreach (var item in late.ReadAsync(cts.Token))
      received.Add(item.Timestamp);

    Assert.That(received, Is.EqualTo(new ulong[] { 9001, 9002 }));
    ((IDisposable)first).Dispose();
  }

  /// <summary>
  /// SCENARIO:
  /// Info property delegates to source
  ///
  /// ACTION:
  /// Read Info
  ///
  /// EXPECTED RESULT:
  /// Returns source's MuxStreamInfo
  /// </summary>
  [Test]
  public void Info_DelegatesToSource()
  {
    var source = new TestMuxStream(TestInfo);
    var fanOut = new MuxStreamFanOut<Fmp4Fragment>(source);

    Assert.That(fanOut.Info.MimeType, Is.EqualTo(TestInfo.MimeType));
    Assert.That(fanOut.Info.Resolution, Is.EqualTo(TestInfo.Resolution));
    Assert.That(fanOut.Info.Fps, Is.EqualTo(TestInfo.Fps));
  }

  /// <summary>
  /// SCENARIO:
  /// Header property delegates to source
  ///
  /// ACTION:
  /// Read Header
  ///
  /// EXPECTED RESULT:
  /// Returns source's Header bytes
  /// </summary>
  [Test]
  public void Header_DelegatesToSource()
  {
    var headerBytes = new byte[] { 0x00, 0x00, 0x00, 0x24, 0x66, 0x74, 0x79, 0x70 };
    var source = new TestMuxStream(TestInfo, headerBytes);
    var fanOut = new MuxStreamFanOut<Fmp4Fragment>(source);

    Assert.That(fanOut.Header.ToArray(), Is.EqualTo(headerBytes));
  }

  private sealed class TestMuxStream : IMuxStream<Fmp4Fragment>
  {
    private readonly Channel<Fmp4Fragment> _channel = Channel.CreateUnbounded<Fmp4Fragment>();

    public MuxStreamInfo Info { get; }
    public ReadOnlyMemory<byte> Header { get; }
    public Type FrameType => typeof(Fmp4Fragment);
    public Action<MuxStreamStats>? OnStats { get; set; }

    public TestMuxStream(MuxStreamInfo info, byte[]? header = null)
    {
      Info = info;
      Header = header ?? ReadOnlyMemory<byte>.Empty;
    }

    public void Emit(Fmp4Fragment fragment) => _channel.Writer.TryWrite(fragment);

    public async IAsyncEnumerable<Fmp4Fragment> ReadAsync(
      [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
      await foreach (var item in _channel.Reader.ReadAllAsync(ct))
        yield return item;
    }
  }
}
