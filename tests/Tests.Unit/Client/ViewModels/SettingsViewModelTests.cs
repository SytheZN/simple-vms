using Client.Core.Events;
using Client.Core.Platform;
using Client.Core.Tunnel;
using Client.Core.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Tests.Unit.Client.Mocks;

namespace Tests.Unit.Client.ViewModels;

[TestFixture]
public class SettingsViewModelTests
{
  /// <summary>
  /// SCENARIO:
  /// LoadAsync is called with stored credentials
  ///
  /// ACTION:
  /// Store credentials and call LoadAsync
  ///
  /// EXPECTED RESULT:
  /// Addresses and ClientId are populated from the credential store
  /// </summary>
  [Test]
  public async Task Load_WithCredentials_PopulatesFields()
  {
    var clientId = Guid.NewGuid();
    var credStore = new MockCredentialStore
    {
      Data = new CredentialData(
        "ca", "cert", "key",
        ["192.168.1.50:4433", "10.0.0.1:4433"],
        clientId)
    };
    var tunnel = new FakeStreamTunnel();
    var router = new NotificationRouter(new FakeEventService(), new MockNotificationService(), NullLogger<NotificationRouter>.Instance);

    var vm = new SettingsViewModel(tunnel, credStore, router, new DiagnosticsInfo(null));
    await vm.LoadAsync();

    Assert.That(vm.Addresses, Is.Not.Null);
    Assert.That(vm.Addresses, Has.Length.EqualTo(2));
    Assert.That(vm.ClientId, Is.EqualTo(clientId));
  }

  /// <summary>
  /// SCENARIO:
  /// LoadAsync is called with no stored credentials
  ///
  /// ACTION:
  /// Call LoadAsync without storing credentials first
  ///
  /// EXPECTED RESULT:
  /// Addresses and ClientId remain null
  /// </summary>
  [Test]
  public async Task Load_NoCredentials_FieldsNull()
  {
    var credStore = new MockCredentialStore();
    var tunnel = new FakeStreamTunnel();
    var router = new NotificationRouter(new FakeEventService(), new MockNotificationService(), NullLogger<NotificationRouter>.Instance);

    var vm = new SettingsViewModel(tunnel, credStore, router, new DiagnosticsInfo(null));
    await vm.LoadAsync();

    Assert.That(vm.Addresses, Is.Null);
    Assert.That(vm.ClientId, Is.Null);
  }

  /// <summary>
  /// SCENARIO:
  /// ConnectionState changes on the tunnel
  ///
  /// ACTION:
  /// Fire StateChanged on the tunnel
  ///
  /// EXPECTED RESULT:
  /// The ViewModel's ConnectionState property updates
  /// </summary>
  [Test]
  public void TunnelStateChanged_UpdatesProperty()
  {
    var credStore = new MockCredentialStore();
    var tunnel = new FakeStreamTunnel();
    var router = new NotificationRouter(new FakeEventService(), new MockNotificationService(), NullLogger<NotificationRouter>.Instance);

    var vm = new SettingsViewModel(tunnel, credStore, router, new DiagnosticsInfo(null));

    var changed = new List<string>();
    vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

    tunnel.FireStateChanged(ConnectionState.Connecting);

    Assert.That(vm.ConnectionState, Is.EqualTo(ConnectionState.Connecting));
    Assert.That(changed, Does.Contain("ConnectionState"));
  }

  /// <summary>
  /// SCENARIO:
  /// DisconnectAsync is called
  ///
  /// ACTION:
  /// Call DisconnectAsync
  ///
  /// EXPECTED RESULT:
  /// Tunnel is disconnected, credentials are cleared, fields are null
  /// </summary>
  [Test]
  public async Task Disconnect_ClearsCredentialsAndDisconnects()
  {
    var credStore = new MockCredentialStore
    {
      Data = new CredentialData(
        "ca", "cert", "key", ["addr:4433"], Guid.NewGuid())
    };
    var tunnel = new FakeStreamTunnel();
    var router = new NotificationRouter(new FakeEventService(), new MockNotificationService(), NullLogger<NotificationRouter>.Instance);

    var vm = new SettingsViewModel(tunnel, credStore, router, new DiagnosticsInfo(null));
    await vm.LoadAsync();
    Assert.That(vm.Addresses, Is.Not.Null);

    await vm.DisconnectAsync();

    Assert.That(vm.Addresses, Is.Null);
    Assert.That(vm.ClientId, Is.Null);
    Assert.That(credStore.Data, Is.Null);
    Assert.That(tunnel.State, Is.EqualTo(ConnectionState.Disconnected));
  }

  /// <summary>
  /// SCENARIO:
  /// Notification rules are saved
  ///
  /// ACTION:
  /// Add rules and call SaveNotificationRules
  ///
  /// EXPECTED RESULT:
  /// The router receives the updated rules
  /// </summary>
  [Test]
  public void SaveNotificationRules_UpdatesRouter()
  {
    var credStore = new MockCredentialStore();
    var tunnel = new FakeStreamTunnel();
    var notifications = new MockNotificationService();
    var eventService = new FakeEventService();
    var router = new NotificationRouter(eventService, notifications, NullLogger<NotificationRouter>.Instance);

    var vm = new SettingsViewModel(tunnel, credStore, router, new DiagnosticsInfo(null));
    var cameraId = Guid.NewGuid();
    vm.NotificationRules.Add(new NotificationRule(cameraId, "motion", true));
    vm.SaveNotificationRules();

    var msg = new Shared.Protocol.EventChannelMessage
    {
      Id = Guid.NewGuid(),
      CameraId = cameraId,
      Type = "motion",
      StartTime = 1_000_000
    };
    eventService.Fire(msg, Shared.Protocol.EventChannelFlags.Start);

    Assert.That(notifications.Calls, Has.Count.EqualTo(1));
  }

  /// <summary>
  /// SCENARIO:
  /// A log path is injected via DiagnosticsInfo
  ///
  /// ACTION:
  /// Construct the view model and read LogFilePath
  ///
  /// EXPECTED RESULT:
  /// The property returns the value supplied via DiagnosticsInfo
  /// </summary>
  [Test]
  public void LogFilePath_PassesThroughFromDiagnosticsInfo()
  {
    var credStore = new MockCredentialStore();
    var tunnel = new FakeStreamTunnel();
    var router = new NotificationRouter(
      new FakeEventService(), new MockNotificationService(), NullLogger<NotificationRouter>.Instance);

    var vm = new SettingsViewModel(tunnel, credStore, router, new DiagnosticsInfo("/tmp/client.log"));

    Assert.That(vm.LogFilePath, Is.EqualTo("/tmp/client.log"));
  }
}
