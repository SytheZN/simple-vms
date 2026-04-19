using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;
using Client.Core.Api;
using Client.Core.Platform;
using Client.Core.Tunnel;
using Microsoft.Extensions.Logging;
using Shared.Models;

namespace Client.Core.ViewModels;

public sealed partial class EnrollmentViewModel : ViewModelBase
{
  private readonly IEnrollmentClient _enrollment;
  private readonly ICredentialStore _credentials;
  private readonly ITunnelService _tunnel;
  private readonly IQrScannerService? _qrScanner;
  private readonly ILogger<EnrollmentViewModel> _logger;

  private string _serverAddress = "";
  private string _token = "";
  private bool _isScanning;
  private bool _isBusy;
  private bool _isEnrolled;

  public string ServerAddress
  {
    get => _serverAddress;
    set
    {
      if (SetProperty(ref _serverAddress, value))
        RaiseCommandsCanExecuteChanged();
    }
  }

  public string Token
  {
    get => _token;
    set
    {
      if (SetProperty(ref _token, value))
        RaiseCommandsCanExecuteChanged();
    }
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
    ILogger<EnrollmentViewModel> logger,
    IServiceProvider services)
  {
    _enrollment = enrollment;
    _credentials = credentials;
    _tunnel = tunnel;
    _logger = logger;
    _qrScanner = services.GetService(typeof(IQrScannerService)) as IQrScannerService;

    ScanQrCommand = new AsyncCommand(ScanQrAsync, () => _qrScanner != null && !IsBusy);
    EnrollCommand = new AsyncCommand(EnrollAsync, () => !IsBusy && ServerAddress.Length > 0 && IsValidToken(Token));
  }

  private const string TokenChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

  private static bool IsValidToken(string token) =>
    token.Length == 9
    && token[4] == '-'
    && token[..4].All(c => TokenChars.Contains(c))
    && token[5..].All(c => TokenChars.Contains(c));

  private void RaiseCommandsCanExecuteChanged()
  {
    (EnrollCommand as AsyncCommand)?.RaiseCanExecuteChanged();
  }

  private async Task ScanQrAsync()
  {
    if (_qrScanner == null) return;

    IsScanning = true;
    ClearError();

    try
    {
      var result = await _qrScanner.ScanAsync(CancellationToken.None);
      if (result == null) return;

      var payload = JsonSerializer.Deserialize(result, QrJsonContext.Default.QrPayload);
      if (payload == null || payload.V != 1)
      {
        var msg = payload?.V > 1 ? "QR code requires a newer client version" : "Invalid QR code";
        SetError(Error.Create(ClientModuleIds.Enrollment, 0x0007, Result.BadRequest, msg));
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
      _logger.LogError(ex, "QR scan failed");
      SetError(Error.Create(ClientModuleIds.Enrollment, 0x0006, Result.InternalError, ex.Message));
    }
    finally
    {
      IsScanning = false;
    }
  }

  private async Task EnrollAsync()
  {
    IsBusy = true;
    ClearError();

    try
    {
      var result = await _enrollment.EnrollAsync(ServerAddress, Token, CancellationToken.None);
      await result.Match(
        async response =>
        {
          var data = new CredentialData(
            response.Ca,
            response.Cert,
            response.Key,
            response.Addresses,
            response.ClientId);
          await CompleteEnrollmentAsync(data);
        },
        httpError =>
        {
          SetError(httpError.Error, httpError.Diagnostics);
          return Task.CompletedTask;
        });
    }
    catch (Exception ex)
    {
      SetError(Error.Create(ClientModuleIds.Enrollment, 0x000A, Result.InternalError, ex.Message));
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
      await _tunnel.ConnectAsync(new(), CancellationToken.None);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Tunnel connection failed after enrollment");
      SetError(Error.Create(ClientModuleIds.Enrollment, 0x0005, Result.Unavailable,
        "Enrolled successfully but failed to connect: " + ex.Message));
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
    private bool _isExecuting;

    public event EventHandler? CanExecuteChanged;

    public AsyncCommand(Func<Task> execute, Func<bool> canExecute)
    {
      _execute = execute;
      _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => !_isExecuting && _canExecute();

    public void RaiseCanExecuteChanged() =>
      CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    public async void Execute(object? parameter)
    {
      if (_isExecuting) return;
      _isExecuting = true;
      CanExecuteChanged?.Invoke(this, EventArgs.Empty);
      try { await _execute(); }
      finally
      {
        _isExecuting = false;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
      }
    }
  }
}
