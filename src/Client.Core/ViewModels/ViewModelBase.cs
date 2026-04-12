using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using Client.Core.Api;
using Shared.Models;

namespace Client.Core.ViewModels;

public abstract class ViewModelBase : INotifyPropertyChanged
{
  public event PropertyChangedEventHandler? PropertyChanged;

  private string? _errorMessage;
  private DebugTag? _errorTag;
  private string? _errorJson;

  public string? ErrorMessage
  {
    get => _errorMessage;
    set => SetProperty(ref _errorMessage, value);
  }

  public DebugTag? ErrorTag
  {
    get => _errorTag;
    set => SetProperty(ref _errorTag, value);
  }

  public string? ErrorJson
  {
    get => _errorJson;
    set => SetProperty(ref _errorJson, value);
  }

  protected void SetError(Error error, HttpDiagnostics? diag = null)
  {
    ErrorMessage = error.Message;
    ErrorTag = error.Tag;

    var parts = new List<string>
    {
      $"\"result\":\"{error.Result}\"",
      $"\"debugTag\":\"{error.Tag}\"",
      $"\"message\":\"{Escape(error.Message)}\""
    };
    if (diag != null)
    {
      parts.Add($"\"url\":\"{Escape(diag.Url)}\"");
      if (diag.StatusCode != null)
        parts.Add($"\"httpStatus\":{diag.StatusCode}");
      if (diag.RawBody != null)
        parts.Add($"\"rawBody\":\"{Escape(diag.RawBody)}\"");
    }
    ErrorJson = "{" + string.Join(",", parts) + "}";
  }

  protected void ClearError()
  {
    ErrorMessage = null;
    ErrorTag = null;
    ErrorJson = null;
  }

  private static string Escape(string s) =>
    s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");

  protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
  {
    if (EqualityComparer<T>.Default.Equals(field, value))
      return false;
    field = value;
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    return true;
  }

  protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

  protected static void RunOnUiThread(Action action)
  {
    if (Dispatcher.UIThread.CheckAccess())
      action();
    else
      Dispatcher.UIThread.Post(action);
  }
}
