using Client.Core.Platform;
using Client.Core.Tunnel;
using Microsoft.Extensions.Logging.Abstractions;
using Tests.Unit.Client.Mocks;
using static Tests.Unit.Client.TunnelServiceTests;

namespace Tests.Unit.Client;

[TestFixture]
public class TunnelServiceExtraTests
{
  private static readonly CredentialData TestCreds = new(
    "ca", "cert", "key",
    ["127.0.0.1:9999", "10.0.0.1:9999", "10.0.0.2:9999"],
    Guid.NewGuid());

  private static TunnelService NewService(
    out MockCredentialStore creds, out MockTransportFactory transport,
    CredentialData? credData = null)
  {
    creds = new MockCredentialStore { Data = credData ?? TestCreds };
    transport = new MockTransportFactory();
    return new TunnelService(creds, transport, NullLogger<TunnelService>.Instance);
  }

  /// <summary>
  /// SCENARIO:
  /// OpenStreamAsync is called before any connection has been established
  ///
  /// ACTION:
  /// New service, immediately call OpenStreamAsync
  ///
  /// EXPECTED RESULT:
  /// Throws InvalidOperationException ("Not connected")
  /// </summary>
  [Test]
  public void OpenStreamAsync_WhileDisconnected_Throws()
  {
    var service = NewService(out _, out _);

    var ex = Assert.ThrowsAsync<InvalidOperationException>(
      () => service.OpenStreamAsync(0, Array.Empty<byte>(), CancellationToken.None));
    Assert.That(ex!.Message, Does.Contain("Not connected"));
  }

  /// <summary>
  /// SCENARIO:
  /// All known addresses fail to connect
  ///
  /// ACTION:
  /// Mark every credential address as failing in the transport, ConnectAsync
  ///
  /// EXPECTED RESULT:
  /// Throws InvalidOperationException with the address count in the message;
  /// State settles back to Disconnected
  /// </summary>
  [Test]
  public void Connect_AllAddressesFail_Throws()
  {
    var service = NewService(out _, out var transport);
    foreach (var a in TestCreds.Addresses) transport.FailAddresses.Add(a);

    var ex = Assert.ThrowsAsync<InvalidOperationException>(
      () => service.ConnectAsync(new(), CancellationToken.None));

    Assert.Multiple(() =>
    {
      Assert.That(ex!.Message, Does.Contain("3"));
      Assert.That(service.State, Is.EqualTo(ConnectionState.Disconnected));
    });
  }

  /// <summary>
  /// SCENARIO:
  /// LastSuccessfulIndex hints the service to try a non-zero address first
  ///
  /// ACTION:
  /// ConnectAsync with options.LastSuccessfulIndex = 1
  ///
  /// EXPECTED RESULT:
  /// The transport receives the index-1 address before any others
  /// </summary>
  [Test]
  public async Task Connect_WithHint_PreferredAddressTriedFirst()
  {
    var service = NewService(out _, out var transport);

    await service.ConnectAsync(new ConnectionOptions { LastSuccessfulIndex = 1 },
      CancellationToken.None);

    Assert.That(transport.ConnectedAddress, Is.EqualTo("10.0.0.1:9999"));

    await service.DisconnectAsync();
  }

  /// <summary>
  /// SCENARIO:
  /// LastSuccessfulIndex points at an address that is currently failing
  ///
  /// ACTION:
  /// Hint index 0, mark address 0 as failing
  ///
  /// EXPECTED RESULT:
  /// Hinted attempt fails; service falls through to the remaining addresses
  /// and connects to the first successful one
  /// </summary>
  [Test]
  public async Task Connect_HintFails_FallsThroughToOthers()
  {
    var service = NewService(out _, out var transport);
    transport.FailAddresses.Add(TestCreds.Addresses[0]);

    await service.ConnectAsync(new ConnectionOptions { LastSuccessfulIndex = 0 },
      CancellationToken.None);

    Assert.That(transport.ConnectedAddress, Is.EqualTo("10.0.0.1:9999"));

    await service.DisconnectAsync();
  }

  /// <summary>
  /// SCENARIO:
  /// DisconnectAsync from a never-connected service
  ///
  /// ACTION:
  /// Call DisconnectAsync immediately after construction
  ///
  /// EXPECTED RESULT:
  /// Completes without throwing; State remains Disconnected
  /// </summary>
  [Test]
  public async Task Disconnect_NeverConnected_NoOp()
  {
    var service = NewService(out _, out _);

    await service.DisconnectAsync();

    Assert.That(service.State, Is.EqualTo(ConnectionState.Disconnected));
  }

  /// <summary>
  /// SCENARIO:
  /// DisposeAsync on a never-connected service
  ///
  /// ACTION:
  /// Call DisposeAsync immediately after construction
  ///
  /// EXPECTED RESULT:
  /// Completes without throwing
  /// </summary>
  [Test]
  public async Task Dispose_NeverConnected_NoOp()
  {
    var service = NewService(out _, out _);

    await service.DisposeAsync();
    await service.DisposeAsync();
  }

  /// <summary>
  /// SCENARIO:
  /// After a clean Disconnect, Connect can be called again
  ///
  /// ACTION:
  /// Connect, Disconnect, Connect
  ///
  /// EXPECTED RESULT:
  /// Second connect succeeds; generation has incremented twice
  /// </summary>
  [Test]
  public async Task Reconnect_AfterDisconnect_Succeeds()
  {
    var service = NewService(out _, out _);

    await service.ConnectAsync(new(), CancellationToken.None);
    await service.DisconnectAsync();
    await service.ConnectAsync(new(), CancellationToken.None);

    Assert.Multiple(() =>
    {
      Assert.That(service.State, Is.EqualTo(ConnectionState.Connected));
      Assert.That(service.Generation, Is.EqualTo(2u));
    });

    await service.DisconnectAsync();
  }

  /// <summary>
  /// SCENARIO:
  /// ConnectedAddressIndex reflects the index of the address that succeeded
  ///
  /// ACTION:
  /// Mark address 0 as failing, Connect
  ///
  /// EXPECTED RESULT:
  /// ConnectedAddressIndex is 1 (the second address)
  /// </summary>
  [Test]
  public async Task ConnectedAddressIndex_ReflectsResolvedAddress()
  {
    var service = NewService(out _, out var transport);
    transport.FailAddresses.Add(TestCreds.Addresses[0]);

    await service.ConnectAsync(new(), CancellationToken.None);

    Assert.That(service.ConnectedAddressIndex, Is.EqualTo(1));

    await service.DisconnectAsync();
  }

  /// <summary>
  /// SCENARIO:
  /// StateChanged emits the full transition sequence on a clean lifecycle
  ///
  /// ACTION:
  /// Subscribe before Connect, run Connect then Disconnect
  ///
  /// EXPECTED RESULT:
  /// Sequence is Connecting, Connected, then Disconnected on Disconnect
  /// </summary>
  [Test]
  public async Task StateChanged_EmitsExpectedSequence()
  {
    var service = NewService(out _, out _);
    var states = new List<ConnectionState>();
    service.StateChanged += states.Add;

    await service.ConnectAsync(new(), CancellationToken.None);
    await service.DisconnectAsync();

    Assert.That(states, Is.EqualTo(new[]
    {
      ConnectionState.Connecting,
      ConnectionState.Connected,
      ConnectionState.Disconnected
    }));
  }
}
