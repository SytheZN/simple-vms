using Client.Core.Tunnel;

namespace Tests.Unit.Client.Tunnel;

[TestFixture]
public class TransportConnectionTests
{
  /// <summary>
  /// SCENARIO:
  /// TransportConnection wraps a stream plus auxiliary disposables (TcpClient)
  ///
  /// ACTION:
  /// Construct with a tracked stream and tracked disposable, call Dispose
  ///
  /// EXPECTED RESULT:
  /// Both the stream and the auxiliary disposable are disposed
  /// </summary>
  [Test]
  public void Dispose_DisposesStreamAndExtras()
  {
    var stream = new TrackingStream();
    var aux = new TrackingDisposable();

    var connection = new TransportConnection(stream, aux);
    connection.Dispose();

    Assert.Multiple(() =>
    {
      Assert.That(stream.Disposed, Is.True);
      Assert.That(aux.Disposed, Is.True);
    });
  }

  /// <summary>
  /// SCENARIO:
  /// DisposeAsync runs the same teardown asynchronously
  ///
  /// ACTION:
  /// Construct with a tracked stream and tracked disposable, await DisposeAsync
  ///
  /// EXPECTED RESULT:
  /// Stream's DisposeAsync is awaited; aux disposable runs synchronously
  /// </summary>
  [Test]
  public async Task DisposeAsync_DisposesStreamAndExtras()
  {
    var stream = new TrackingStream();
    var aux = new TrackingDisposable();

    var connection = new TransportConnection(stream, aux);
    await connection.DisposeAsync();

    Assert.Multiple(() =>
    {
      Assert.That(stream.Disposed, Is.True);
      Assert.That(aux.Disposed, Is.True);
    });
  }

  /// <summary>
  /// SCENARIO:
  /// TransportConnection is constructed with no auxiliary disposables
  ///
  /// ACTION:
  /// Dispose without aux objects
  ///
  /// EXPECTED RESULT:
  /// Stream is disposed; no exception from the empty auxiliary loop
  /// </summary>
  [Test]
  public void Dispose_NoAuxiliaries_OnlyDisposesStream()
  {
    var stream = new TrackingStream();

    var connection = new TransportConnection(stream);
    connection.Dispose();

    Assert.That(stream.Disposed, Is.True);
  }

  private sealed class TrackingStream : MemoryStream
  {
    public bool Disposed { get; private set; }
    protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
  }

  private sealed class TrackingDisposable : IDisposable
  {
    public bool Disposed { get; private set; }
    public void Dispose() => Disposed = true;
  }
}
