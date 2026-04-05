using Client.Core;
using Client.Core.Api;
using Client.Core.Platform;
using Client.Core.Tunnel;
using Client.Core.ViewModels;
using Shared.Models;
using Shared.Models.Dto;
using Tests.Unit.Client.Mocks;

namespace Tests.Unit.Client.ViewModels;

[TestFixture]
public class EnrollmentViewModelTests
{
  /// <summary>
  /// SCENARIO:
  /// EnrollCommand is executed with valid address and token
  ///
  /// ACTION:
  /// Set ServerAddress and Token, execute EnrollCommand
  ///
  /// EXPECTED RESULT:
  /// Credentials are saved, tunnel connects, IsEnrolled is true
  /// </summary>
  [Test]
  public async Task Enroll_Success_SavesCredentialsAndConnects()
  {
    var enrollClient = new FakeEnrollmentClient
    {
      Response = new EnrollResponse
      {
        Addresses = ["127.0.0.1:4433"],
        Ca = "-----BEGIN CERTIFICATE-----\nCA\n-----END CERTIFICATE-----",
        Cert = "-----BEGIN CERTIFICATE-----\nCERT\n-----END CERTIFICATE-----",
        Key = "-----BEGIN PRIVATE KEY-----\nKEY\n-----END PRIVATE KEY-----",
        ClientId = Guid.NewGuid()
      }
    };
    var credStore = new MockCredentialStore();
    var tunnel = new FakeStreamTunnel();

    var vm = new EnrollmentViewModel(enrollClient, credStore, tunnel, EmptyServices());
    vm.ServerAddress = "myserver:8080";
    vm.Token = "ABCD-1234";

    vm.EnrollCommand.Execute(null);
    await Task.Delay(200);

    Assert.That(credStore.Data, Is.Not.Null);
    Assert.That(tunnel.State, Is.EqualTo(ConnectionState.Connected));
    Assert.That(vm.IsEnrolled, Is.True);
    Assert.That(vm.ErrorMessage, Is.Null);
  }

  /// <summary>
  /// SCENARIO:
  /// Enrollment fails with an error
  ///
  /// ACTION:
  /// Set ServerAddress and Token, execute EnrollCommand with a failing client
  ///
  /// EXPECTED RESULT:
  /// ErrorMessage is set, IsEnrolled remains false
  /// </summary>
  [Test]
  public async Task Enroll_Failure_SetsErrorMessage()
  {
    var enrollClient = new FakeEnrollmentClient
    {
      Error = new Error(Result.Unavailable, new DebugTag(ClientModuleIds.Enrollment, 0x0001), "Connection refused")
    };
    var credStore = new MockCredentialStore();
    var tunnel = new FakeStreamTunnel();

    var vm = new EnrollmentViewModel(enrollClient, credStore, tunnel, EmptyServices());
    vm.ServerAddress = "badserver:8080";
    vm.Token = "ABCD-1234";

    vm.EnrollCommand.Execute(null);
    await Task.Delay(200);

    Assert.That(vm.ErrorMessage, Is.EqualTo("Connection refused"));
    Assert.That(vm.IsEnrolled, Is.False);
    Assert.That(credStore.Data, Is.Null);
  }

  /// <summary>
  /// SCENARIO:
  /// QR scanner returns a valid payload
  ///
  /// ACTION:
  /// Execute ScanQrCommand with a mock scanner that returns valid JSON
  ///
  /// EXPECTED RESULT:
  /// ServerAddress and Token are populated from the QR payload
  /// </summary>
  [Test]
  public async Task ScanQr_ValidPayload_PopulatesFields()
  {
    var enrollClient = new FakeEnrollmentClient();
    var credStore = new MockCredentialStore();
    var tunnel = new FakeStreamTunnel();
    var scanner = new FakeQrScanner
    {
      Result = """{"v":1,"addresses":["192.168.1.50:8080"],"token":"X7K2-M9P4"}"""
    };

    var vm = new EnrollmentViewModel(enrollClient, credStore, tunnel, ServicesWithQr(scanner));

    vm.ScanQrCommand.Execute(null);
    await Task.Delay(100);

    Assert.That(vm.ServerAddress, Is.EqualTo("192.168.1.50:8080"));
    Assert.That(vm.Token, Is.EqualTo("X7K2-M9P4"));
  }

  /// <summary>
  /// SCENARIO:
  /// QR scanner returns null (user cancelled)
  ///
  /// ACTION:
  /// Execute ScanQrCommand with a scanner that returns null
  ///
  /// EXPECTED RESULT:
  /// ServerAddress and Token remain empty
  /// </summary>
  [Test]
  public async Task ScanQr_Cancelled_NoChange()
  {
    var enrollClient = new FakeEnrollmentClient();
    var credStore = new MockCredentialStore();
    var tunnel = new FakeStreamTunnel();
    var scanner = new FakeQrScanner { Result = null };

    var vm = new EnrollmentViewModel(enrollClient, credStore, tunnel, ServicesWithQr(scanner));

    vm.ScanQrCommand.Execute(null);
    await Task.Delay(100);

    Assert.That(vm.ServerAddress, Is.EqualTo(""));
    Assert.That(vm.Token, Is.EqualTo(""));
  }

  /// <summary>
  /// SCENARIO:
  /// PropertyChanged fires for observable properties
  ///
  /// ACTION:
  /// Set ServerAddress, Token, IsBusy, ErrorMessage
  ///
  /// EXPECTED RESULT:
  /// PropertyChanged fires for each property
  /// </summary>
  [Test]
  public void Properties_Set_FirePropertyChanged()
  {
    var vm = new EnrollmentViewModel(
      new FakeEnrollmentClient(), new MockCredentialStore(), new FakeStreamTunnel(), EmptyServices());

    var changed = new List<string>();
    vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

    vm.ServerAddress = "test";
    vm.Token = "ABCD";
    vm.ErrorMessage = "oops";

    Assert.That(changed, Does.Contain("ServerAddress"));
    Assert.That(changed, Does.Contain("Token"));
    Assert.That(changed, Does.Contain("ErrorMessage"));
  }

  private sealed class FakeEnrollmentClient : IEnrollmentClient
  {
    public EnrollResponse? Response { get; set; }
    public Error? Error { get; set; }

    public Task<OneOf<EnrollResponse, Shared.Models.Error>> EnrollAsync(
      string serverAddress, string token, CancellationToken ct)
    {
      if (Error != null)
        return Task.FromResult<OneOf<EnrollResponse, Shared.Models.Error>>(Error.Value);
      return Task.FromResult<OneOf<EnrollResponse, Shared.Models.Error>>(Response!);
    }
  }

  private sealed class FakeQrScanner : IQrScannerService
  {
    public string? Result { get; set; }

    public Task<string?> ScanAsync(CancellationToken ct) =>
      Task.FromResult(Result);
  }

  private static IServiceProvider EmptyServices() =>
    new SimpleServiceProvider(null);

  private static IServiceProvider ServicesWithQr(FakeQrScanner scanner) =>
    new SimpleServiceProvider(scanner);

  private sealed class SimpleServiceProvider(IQrScannerService? qr) : IServiceProvider
  {
    public object? GetService(Type serviceType) =>
      serviceType == typeof(IQrScannerService) ? qr : null;
  }
}
