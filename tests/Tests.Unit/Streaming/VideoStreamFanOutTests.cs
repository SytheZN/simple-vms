using System.Threading.Channels;
using Server.Streaming;
using Shared.Models;
using Shared.Models.Formats;

namespace Tests.Unit.Streaming;

[TestFixture]
public class VideoStreamFanOutTests
{
  private static VideoStreamInfo TestInfo => new()
  {
    DataFormat = "fmp4",
    MimeType = "video/mp4; codecs=\"avc1.640029\"",
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
  /// OnDemand is set, first subscriber is added
  ///
  /// ACTION:
  /// Subscribe
  ///
  /// EXPECTED RESULT:
  /// OnDemand fires
  /// </summary>
  [Test]
  public async Task Subscribe_FirstSubscriber_FiresOnDemand()
  {
    var source = new TestVideoStream(TestInfo);
    await using var fanOut = new VideoStreamFanOut<Fmp4Fragment>(source);

    var demandFired = false;
    fanOut.OnDemand = () => demandFired = true;

    var sub = fanOut.Subscribe();
    Assert.That(demandFired, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// Two subscribers added
  ///
  /// ACTION:
  /// Subscribe twice
  ///
  /// EXPECTED RESULT:
  /// OnDemand fires only on the first subscriber
  /// </summary>
  [Test]
  public async Task Subscribe_SecondSubscriber_DoesNotFireOnDemand()
  {
    var source = new TestVideoStream(TestInfo);
    await using var fanOut = new VideoStreamFanOut<Fmp4Fragment>(source);

    var demandCount = 0;
    fanOut.OnDemand = () => demandCount++;

    fanOut.Subscribe();
    fanOut.Subscribe();

    Assert.That(demandCount, Is.EqualTo(1));
  }

  /// <summary>
  /// SCENARIO:
  /// Last subscriber is removed
  ///
  /// ACTION:
  /// Subscribe, then the subscriber's ReadAsync completes
  ///
  /// EXPECTED RESULT:
  /// OnEmpty fires
  /// </summary>
  [Test]
  public async Task LastSubscriberLeaves_FiresOnEmpty()
  {
    var source = new TestVideoStream(TestInfo);
    await using var fanOut = new VideoStreamFanOut<Fmp4Fragment>(source);

    var emptyFired = false;
    fanOut.OnDemand = () => { };
    fanOut.OnEmpty = () => emptyFired = true;

    var sub = fanOut.Subscribe();

    using var cts = new CancellationTokenSource(50);
    try
    {
      await foreach (var _ in ((IVideoStream<Fmp4Fragment>)sub).ReadAsync(cts.Token)) { }
    }
    catch (OperationCanceledException) { }

    Assert.That(emptyFired, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// One of two subscribers leaves
  ///
  /// ACTION:
  /// Subscribe twice, first subscriber's ReadAsync completes
  ///
  /// EXPECTED RESULT:
  /// OnEmpty does not fire
  /// </summary>
  [Test]
  public async Task OneOfTwoSubscribersLeaves_DoesNotFireOnEmpty()
  {
    var source = new TestVideoStream(TestInfo);
    await using var fanOut = new VideoStreamFanOut<Fmp4Fragment>(source);

    var emptyFired = false;
    fanOut.OnDemand = () => { };
    fanOut.OnEmpty = () => emptyFired = true;

    var sub1 = fanOut.Subscribe();
    var sub2 = fanOut.Subscribe();

    using var cts = new CancellationTokenSource(50);
    try
    {
      await foreach (var _ in ((IVideoStream<Fmp4Fragment>)sub1).ReadAsync(cts.Token)) { }
    }
    catch (OperationCanceledException) { }

    Assert.That(emptyFired, Is.False);
    Assert.That(fanOut.SubscriberCount, Is.EqualTo(1));
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
    var source = new TestVideoStream(TestInfo);
    await using var fanOut = new VideoStreamFanOut<Fmp4Fragment>(source);
    fanOut.OnDemand = () => { };

    var sub = fanOut.Subscribe();

    source.Emit(MakeFragment(1, sync: false));
    source.Emit(MakeFragment(2, sync: false));
    source.Emit(MakeFragment(3, sync: true));
    source.Emit(MakeFragment(4, sync: false));

    await Task.Delay(100);

    var received = new List<ulong>();
    using var cts = new CancellationTokenSource(100);
    try
    {
      await foreach (var item in ((IVideoStream<Fmp4Fragment>)sub).ReadAsync(cts.Token))
        received.Add(item.Timestamp);
    }
    catch (OperationCanceledException) { }

    Assert.That(received, Does.Contain(3UL));
    Assert.That(received, Does.Not.Contain(1UL));
    Assert.That(received, Does.Not.Contain(2UL));
  }

  /// <summary>
  /// SCENARIO:
  /// Info property delegates to source
  ///
  /// ACTION:
  /// Read Info
  ///
  /// EXPECTED RESULT:
  /// Returns source's VideoStreamInfo
  /// </summary>
  [Test]
  public void Info_DelegatesToSource()
  {
    var source = new TestVideoStream(TestInfo);
    var fanOut = new VideoStreamFanOut<Fmp4Fragment>(source);

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
    var source = new TestVideoStream(TestInfo, headerBytes);
    var fanOut = new VideoStreamFanOut<Fmp4Fragment>(source);

    Assert.That(fanOut.Header.ToArray(), Is.EqualTo(headerBytes));
  }

  private sealed class TestVideoStream : IVideoStream<Fmp4Fragment>
  {
    private readonly Channel<Fmp4Fragment> _channel = Channel.CreateUnbounded<Fmp4Fragment>();

    public VideoStreamInfo Info { get; }
    public ReadOnlyMemory<byte> Header { get; }
    public Type FrameType => typeof(Fmp4Fragment);

    public TestVideoStream(VideoStreamInfo info, byte[]? header = null)
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
