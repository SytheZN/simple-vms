using System.Collections.ObjectModel;
using Client.Core.Events;
using Client.Core.Platform;
using Client.Core.Tunnel;

namespace Client.Core.ViewModels;

public sealed class SettingsViewModel : ViewModelBase, IDisposable
{
  private readonly ITunnelService _tunnel;
  private readonly ICredentialStore _credentials;
  private readonly NotificationRouter _router;
  private readonly DiagnosticsInfo _diagnostics;

  private ConnectionState _connectionState;
  private string[]? _addresses;
  private Guid? _clientId;
  private bool _confirmingUnregister;

  public ObservableCollection<NotificationRule> NotificationRules { get; } = [];

  public ConnectionState ConnectionState
  {
    get => _connectionState;
    private set => SetProperty(ref _connectionState, value);
  }

  public string[]? Addresses
  {
    get => _addresses;
    private set => SetProperty(ref _addresses, value);
  }

  public Guid? ClientId
  {
    get => _clientId;
    private set => SetProperty(ref _clientId, value);
  }

  public bool ConfirmingUnregister
  {
    get => _confirmingUnregister;
    private set
    {
      if (SetProperty(ref _confirmingUnregister, value))
        OnPropertyChanged(nameof(IsNotConfirmingUnregister));
    }
  }

  public bool IsNotConfirmingUnregister => !_confirmingUnregister;

  public string? LogFilePath => _diagnostics.LogFilePath;

  public string Version => _diagnostics.Version;

  public SettingsViewModel(
    ITunnelService tunnel,
    ICredentialStore credentials,
    NotificationRouter router,
    DiagnosticsInfo diagnostics)
  {
    _tunnel = tunnel;
    _credentials = credentials;
    _router = router;
    _diagnostics = diagnostics;
    _tunnel.StateChanged += OnTunnelStateChanged;
    ConnectionState = _tunnel.State;
  }

  public async Task LoadAsync()
  {
    var creds = await _credentials.LoadAsync();
    if (creds != null)
    {
      Addresses = creds.Addresses;
      ClientId = creds.ClientId;
    }
  }

  public void SaveNotificationRules()
  {
    _router.UpdateRules(NotificationRules.ToList());
  }

  public async Task ReconnectAsync()
  {
    ClearError();
    try
    {
      await _tunnel.DisconnectAsync();
      await _tunnel.ConnectAsync(new ConnectionOptions(), CancellationToken.None);
    }
    catch (Exception ex)
    {
      ErrorMessage = $"Reconnect failed: {ex.Message}";
    }
  }

  public void BeginUnregister() => ConfirmingUnregister = true;

  public void CancelUnregister() => ConfirmingUnregister = false;

  public async Task UnregisterAsync()
  {
    ConfirmingUnregister = false;
    await _tunnel.DisconnectAsync();
    await _credentials.ClearAsync();
    Addresses = null;
    ClientId = null;
  }

  private void OnTunnelStateChanged(ConnectionState state) =>
    RunOnUiThread(() => ConnectionState = state);

  public void Dispose() => _tunnel.StateChanged -= OnTunnelStateChanged;
}
