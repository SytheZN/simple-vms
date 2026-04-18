using Client.Core.Api;
using Client.Core.Platform;
using Client.Core.Tunnel;
using Client.Core.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Models;
using Shared.Models.Dto;
using Tests.Unit.Client.Mocks;

namespace Tests.Unit.Client.ViewModels;

[TestFixture]
public class EnrollmentViewModelExtraTests
{
  /// <summary>
  /// SCENARIO:
  /// EnrollCommand.CanExecute checks token validity (4-char dash 4-char from
  /// the restricted alphabet)
  ///
  /// ACTION:
  /// Probe a range of token strings
  ///
  /// EXPECTED RESULT:
  /// Only well-formed tokens with permissible characters return true
  /// </summary>
  [Test]
  public void EnrollCommand_CanExecute_HonoursTokenFormat()
  {
    var vm = NewVm(out _, out _);
    vm.ServerAddress = "host:8080";

    Assert.Multiple(() =>
    {
      vm.Token = "ABCD-2345";
      Assert.That(vm.EnrollCommand.CanExecute(null), Is.True, "valid token");

      vm.Token = "abcd-2345";
      Assert.That(vm.EnrollCommand.CanExecute(null), Is.False, "lowercase rejected");

      vm.Token = "ABCD2345";
      Assert.That(vm.EnrollCommand.CanExecute(null), Is.False, "missing dash rejected");

      vm.Token = "ABCD-234";
      Assert.That(vm.EnrollCommand.CanExecute(null), Is.False, "too short rejected");

      vm.Token = "ABCD-1IO0";
      Assert.That(vm.EnrollCommand.CanExecute(null), Is.False, "ambiguous chars rejected");
    });
  }

  /// <summary>
  /// SCENARIO:
  /// EnrollCommand.CanExecute requires a non-empty server address
  ///
  /// ACTION:
  /// Set valid token, leave address empty
  ///
  /// EXPECTED RESULT:
  /// CanExecute returns false
  /// </summary>
  [Test]
  public void EnrollCommand_CanExecute_RequiresAddress()
  {
    var vm = NewVm(out _, out _);
    vm.Token = "ABCD-1234";

    Assert.That(vm.EnrollCommand.CanExecute(null), Is.False);
  }

  /// <summary>
  /// SCENARIO:
  /// ScanQrCommand has no scanner registered (desktop without camera support)
  ///
  /// ACTION:
  /// Construct VM without IQrScannerService, check CanExecute
  ///
  /// EXPECTED RESULT:
  /// CanExecute is false (no scanner -> command disabled)
  /// </summary>
  [Test]
  public void ScanQrCommand_NoScanner_CannotExecute()
  {
    var vm = NewVm(out _, out _);

    Assert.That(vm.ScanQrCommand.CanExecute(null), Is.False);
  }

  /// <summary>
  /// SCENARIO:
  /// QR payload uses a future protocol version
  ///
  /// ACTION:
  /// Scan a payload with V = 2
  ///
  /// EXPECTED RESULT:
  /// SetError fires with a "newer client version" message
  /// </summary>
  [Test]
  public async Task ScanQr_NewerVersion_SetsUpgradeError()
  {
    var scanner = new FakeQrScanner { Result = """{"v":2,"addresses":["x"],"token":"X7K2-M9P4"}""" };
    var vm = new EnrollmentViewModel(new FakeEnrollmentClient(), new MockCredentialStore(),
      new FakeStreamTunnel(), NullLogger<EnrollmentViewModel>.Instance,
      ServicesWithQr(scanner));

    vm.ScanQrCommand.Execute(null);
    await Task.Delay(100);

    Assert.That(vm.ErrorMessage, Does.Contain("newer client version"));
  }

  /// <summary>
  /// SCENARIO:
  /// QR payload is malformed JSON
  ///
  /// ACTION:
  /// Scan a payload that fails JSON parse
  ///
  /// EXPECTED RESULT:
  /// SetError fires; address/token stay empty
  /// </summary>
  [Test]
  public async Task ScanQr_MalformedJson_SetsError()
  {
    var scanner = new FakeQrScanner { Result = "not json at all" };
    var vm = new EnrollmentViewModel(new FakeEnrollmentClient(), new MockCredentialStore(),
      new FakeStreamTunnel(), NullLogger<EnrollmentViewModel>.Instance,
      ServicesWithQr(scanner));

    vm.ScanQrCommand.Execute(null);
    await Task.Delay(100);

    Assert.Multiple(() =>
    {
      Assert.That(vm.ErrorMessage, Is.Not.Null);
      Assert.That(vm.ServerAddress, Is.Empty);
    });
  }

  /// <summary>
  /// SCENARIO:
  /// Enrollment succeeds but the post-enrollment tunnel connect throws
  ///
  /// ACTION:
  /// Use a credential store that holds the data, but a tunnel whose
  /// ConnectAsync throws after the credentials save
  ///
  /// EXPECTED RESULT:
  /// IsEnrolled is still true (credentials were saved); ErrorMessage carries
  /// the connect failure reason
  /// </summary>
  [Test]
  public async Task Enroll_SuccessButConnectFails_SetsError()
  {
    var enroll = new FakeEnrollmentClient
    {
      Response = new EnrollResponse
      {
        Addresses = ["host:1"], Ca = "ca", Cert = "cert", Key = "key",
        ClientId = Guid.NewGuid()
      }
    };
    var creds = new MockCredentialStore();
    var tunnel = new ThrowingTunnel("tunnel down");

    var vm = new EnrollmentViewModel(enroll, creds, tunnel,
      NullLogger<EnrollmentViewModel>.Instance, EmptyServices());
    vm.ServerAddress = "host:1";
    vm.Token = "ABCD-1234";

    vm.EnrollCommand.Execute(null);
    await Task.Delay(200);

    Assert.Multiple(() =>
    {
      Assert.That(vm.IsEnrolled, Is.True);
      Assert.That(vm.ErrorMessage, Does.Contain("tunnel down"));
    });
  }

  private static EnrollmentViewModel NewVm(out FakeEnrollmentClient enroll, out MockCredentialStore creds)
  {
    enroll = new FakeEnrollmentClient();
    creds = new MockCredentialStore();
    return new EnrollmentViewModel(enroll, creds, new FakeStreamTunnel(),
      NullLogger<EnrollmentViewModel>.Instance, EmptyServices());
  }

  private static IServiceProvider EmptyServices() => new SimpleServiceProvider(null);
  private static IServiceProvider ServicesWithQr(FakeQrScanner scanner) =>
    new SimpleServiceProvider(scanner);

  private sealed class FakeEnrollmentClient : IEnrollmentClient
  {
    public EnrollResponse? Response { get; set; }
    public Error? Error { get; set; }
    public Task<OneOf<EnrollResponse, HttpError>> EnrollAsync(
      string serverAddress, string token, CancellationToken ct)
    {
      if (Error != null)
        return Task.FromResult<OneOf<EnrollResponse, HttpError>>(new HttpError(Error.Value, null));
      return Task.FromResult<OneOf<EnrollResponse, HttpError>>(Response!);
    }
  }

  private sealed class FakeQrScanner : IQrScannerService
  {
    public string? Result { get; set; }
    public Task<string?> ScanAsync(CancellationToken ct) => Task.FromResult(Result);
  }

  private sealed class SimpleServiceProvider(IQrScannerService? qr) : IServiceProvider
  {
    public object? GetService(Type serviceType) =>
      serviceType == typeof(IQrScannerService) ? qr : null;
  }

  private sealed class ThrowingTunnel(string message) : ITunnelService
  {
    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
    public event Action<ConnectionState>? StateChanged;
    public uint Generation => 0;
    public int ConnectedAddressIndex => -1;

    public Task ConnectAsync(ConnectionOptions options, CancellationToken ct)
    {
      State = ConnectionState.Connecting;
      StateChanged?.Invoke(State);
      throw new InvalidOperationException(message);
    }
    public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task<MuxStream> OpenStreamAsync(ushort t, ReadOnlyMemory<byte> p, CancellationToken ct) =>
      throw new InvalidOperationException("not connected");
  }
}
