using Client.Core;
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

    var vm = new EnrollmentViewModel(enrollClient, credStore, tunnel, NullLogger<EnrollmentViewModel>.Instance, EmptyServices());
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

    var vm = new EnrollmentViewModel(enrollClient, credStore, tunnel, NullLogger<EnrollmentViewModel>.Instance, EmptyServices());
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

    var vm = new EnrollmentViewModel(enrollClient, credStore, tunnel, NullLogger<EnrollmentViewModel>.Instance, ServicesWithQr(scanner));

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

    var vm = new EnrollmentViewModel(enrollClient, credStore, tunnel, NullLogger<EnrollmentViewModel>.Instance, ServicesWithQr(scanner));

    vm.ScanQrCommand.Execute(null);
    await Task.Delay(100);

    Assert.That(vm.ServerAddress, Is.EqualTo(""));
    Assert.That(vm.Token, Is.EqualTo(""));
  }

  /// <summary>
  /// SCENARIO:
  /// User pastes a lowercase token
  ///
  /// ACTION:
  /// Assign lowercase alphabetic input to Token
  ///
  /// EXPECTED RESULT:
  /// Value is normalized to uppercase with dash inserted
  /// </summary>
  [Test]
  public void Token_LowercasePaste_UppercasesAndInsertsDash()
  {
    var vm = NewVm();

    vm.Token = "abcd2345";

    Assert.That(vm.Token, Is.EqualTo("ABCD-2345"));
  }

  /// <summary>
  /// SCENARIO:
  /// User pastes a token that already contains the canonical dash
  ///
  /// ACTION:
  /// Assign "ABCD-2345" to Token
  ///
  /// EXPECTED RESULT:
  /// Dash is stripped then reinserted at position 4; final value unchanged
  /// </summary>
  [Test]
  public void Token_PasteWithDash_PreservesCanonicalForm()
  {
    var vm = NewVm();

    vm.Token = "ABCD-2345";

    Assert.That(vm.Token, Is.EqualTo("ABCD-2345"));
  }

  /// <summary>
  /// SCENARIO:
  /// User pastes a token copied from chat software with stray whitespace
  ///
  /// ACTION:
  /// Assign "  abcd 2345\t" to Token
  ///
  /// EXPECTED RESULT:
  /// Whitespace is stripped and the token is normalized
  /// </summary>
  [Test]
  public void Token_PasteWithWhitespace_StripsAndNormalizes()
  {
    var vm = NewVm();

    vm.Token = "  abcd 2345\t";

    Assert.That(vm.Token, Is.EqualTo("ABCD-2345"));
  }

  /// <summary>
  /// SCENARIO:
  /// User pastes a token where characters outside the alphabet have been substituted
  /// (0 for O, 1 for I, etc.)
  ///
  /// ACTION:
  /// Assign an input mixing valid chars with 0, O, 1, I
  ///
  /// EXPECTED RESULT:
  /// Disallowed characters are dropped; no silent substitution occurs
  /// </summary>
  [Test]
  public void Token_PasteWithDisallowedChars_DropsThem()
  {
    var vm = NewVm();

    vm.Token = "A0BO1IDC2345";

    Assert.That(vm.Token, Is.EqualTo("ABDC-2345"));
  }

  /// <summary>
  /// SCENARIO:
  /// User types the token one character at a time
  ///
  /// ACTION:
  /// Assign progressively longer strings mimicking keystrokes
  ///
  /// EXPECTED RESULT:
  /// Dash appears once the fifth valid character is entered
  /// </summary>
  [Test]
  public void Token_IncrementalTyping_InsertsDashAtFifthChar()
  {
    var vm = NewVm();

    vm.Token = "A";     Assert.That(vm.Token, Is.EqualTo("A"));
    vm.Token = "AB";    Assert.That(vm.Token, Is.EqualTo("AB"));
    vm.Token = "ABC";   Assert.That(vm.Token, Is.EqualTo("ABC"));
    vm.Token = "ABCD";  Assert.That(vm.Token, Is.EqualTo("ABCD"));
    vm.Token = "ABCD2"; Assert.That(vm.Token, Is.EqualTo("ABCD-2"));
    vm.Token = "ABCD23";Assert.That(vm.Token, Is.EqualTo("ABCD-23"));
  }

  /// <summary>
  /// SCENARIO:
  /// User pastes more than 8 valid characters
  ///
  /// ACTION:
  /// Assign a 12-char all-valid string to Token
  ///
  /// EXPECTED RESULT:
  /// Token is truncated to 8 valid characters with dash between positions 4 and 5
  /// </summary>
  [Test]
  public void Token_OverLengthInput_TruncatesToEightValidChars()
  {
    var vm = NewVm();

    vm.Token = "ABCDEFGHJKLM";

    Assert.That(vm.Token, Is.EqualTo("ABCD-EFGH"));
  }

  /// <summary>
  /// SCENARIO:
  /// Input containing only disallowed characters is assigned
  ///
  /// ACTION:
  /// Assign "01OI!@#" to Token
  ///
  /// EXPECTED RESULT:
  /// Token becomes empty string
  /// </summary>
  [Test]
  public void Token_AllDisallowed_BecomesEmpty()
  {
    var vm = NewVm();
    vm.Token = "ABCD";

    vm.Token = "01OI!@#";

    Assert.That(vm.Token, Is.EqualTo(""));
    Assert.That(vm.EnrollCommand.CanExecute(null), Is.False);
  }

  private static EnrollmentViewModel NewVm() =>
    new(new FakeEnrollmentClient(), new MockCredentialStore(), new FakeStreamTunnel(),
      NullLogger<EnrollmentViewModel>.Instance, EmptyServices());

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
      new FakeEnrollmentClient(), new MockCredentialStore(), new FakeStreamTunnel(),
      NullLogger<EnrollmentViewModel>.Instance, EmptyServices());

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
    public bool IsAvailable => true;

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
