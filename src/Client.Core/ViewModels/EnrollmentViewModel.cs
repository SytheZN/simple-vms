using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;
using Client.Core.Api;
using Client.Core.Platform;
using Client.Core.Tunnel;

namespace Client.Core.ViewModels;

public sealed partial class EnrollmentViewModel : ViewModelBase
{
  private readonly IEnrollmentClient _enrollment;
  private readonly ICredentialStore _credentials;
  private readonly ITunnelService _tunnel;
  private readonly IQrScannerService? _qrScanner;

  private string _serverAddress = "";
  private string _token = "";
  private bool _isScanning;
  private bool _isBusy;
  private string? _errorMessage;
  private bool _isEnrolled;

  public string ServerAddress
  {
    get => _serverAddress;
    set => SetProperty(ref _serverAddress, value);
  }

  public string Token
  {
    get => _token;
    set => SetProperty(ref _token, value);
  }

  public bool IsScanning
  {
    get => _isScanning;
    set => SetProperty(ref _isScanning, value);
  }

  public bool IsBusy
  {
    get => _isBusy;
    set => SetProperty(ref _isBusy, value);
  }

  public string? ErrorMessage
  {
    get => _errorMessage;
    set => SetProperty(ref _errorMessage, value);
  }

  public bool IsEnrolled
  {
    get => _isEnrolled;
    set => SetProperty(ref _isEnrolled, value);
  }

  public ICommand ScanQrCommand { get; }
  public ICommand EnrollCommand { get; }

  public EnrollmentViewModel(
    IEnrollmentClient enrollment,
    ICredentialStore credentials,
    ITunnelService tunnel,
    IServiceProvider services)
  {
    _enrollment = enrollment;
    _credentials = credentials;
    _tunnel = tunnel;
    _qrScanner = services.GetService(typeof(IQrScannerService)) as IQrScannerService;

    ScanQrCommand = new AsyncCommand(ScanQrAsync, () => _qrScanner != null && !IsBusy, e => ErrorMessage = e);
    EnrollCommand = new AsyncCommand(EnrollAsync, () => !IsBusy && ServerAddress.Length > 0 && Token.Length > 0, e => ErrorMessage = e);
  }

  private async Task ScanQrAsync()
  {
    if (_qrScanner == null) return;

    IsScanning = true;
    ErrorMessage = null;

    try
    {
      var result = await _qrScanner.ScanAsync(CancellationToken.None);
      if (result == null) return;

      var payload = JsonSerializer.Deserialize(result, QrJsonContext.Default.QrPayload);
      if (payload == null || payload.V != 1)
      {
        ErrorMessage = payload?.V > 1 ? "QR code requires a newer client version" : "Invalid QR code";
        return;
      }
      if (payload.Addresses is { Length: > 0 } && payload.Token != null)
      {
        ServerAddress = payload.Addresses[0];
        Token = payload.Token;
      }
    }
    catch (Exception ex)
    {
      ErrorMessage = ex.Message;
    }
    finally
    {
      IsScanning = false;
    }
  }

  private async Task EnrollAsync()
  {
    IsBusy = true;
    ErrorMessage = null;

    try
    {
      var result = await _enrollment.EnrollAsync(ServerAddress, Token, CancellationToken.None);
      if (result.IsT1)
      {
        ErrorMessage = result.AsT1.Message;
        return;
      }

      var response = result.AsT0;
      var data = new CredentialData(
        response.Ca,
        response.Cert,
        response.Key,
        response.Addresses,
        response.ClientId);
      await CompleteEnrollmentAsync(data);
    }
    catch (Exception ex)
    {
      ErrorMessage = ex.Message;
    }
    finally
    {
      IsBusy = false;
    }
  }

  private async Task CompleteEnrollmentAsync(CredentialData data)
  {
    await _credentials.SaveAsync(data);
    IsEnrolled = true;
    try
    {
      await _tunnel.ConnectAsync(CancellationToken.None);
    }
    catch
    {
      ErrorMessage = "Enrolled successfully but failed to connect. Retry from settings.";
    }
  }

  internal sealed class QrPayload
  {
    public int V { get; set; }
    public string[]? Addresses { get; set; }
    public string? Token { get; set; }
  }

  [JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
  [JsonSerializable(typeof(QrPayload))]
  [ExcludeFromCodeCoverage]
  internal sealed partial class QrJsonContext : JsonSerializerContext;

  private sealed class AsyncCommand : ICommand
  {
    private readonly Func<Task> _execute;
    private readonly Func<bool> _canExecute;
    private readonly Action<string>? _onError;
    private bool _isExecuting;

    public event EventHandler? CanExecuteChanged;

    public AsyncCommand(Func<Task> execute, Func<bool> canExecute, Action<string>? onError = null)
    {
      _execute = execute;
      _canExecute = canExecute;
      _onError = onError;
    }

    public bool CanExecute(object? parameter) => !_isExecuting && _canExecute();

    public async void Execute(object? parameter)
    {
      if (_isExecuting) return;
      _isExecuting = true;
      CanExecuteChanged?.Invoke(this, EventArgs.Empty);
      try { await _execute(); }
      catch (Exception ex)
      {
        _onError?.Invoke(ex.Message);
      }
      finally
      {
        _isExecuting = false;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
      }
    }
  }
}
