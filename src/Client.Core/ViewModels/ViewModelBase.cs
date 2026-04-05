using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Threading;

namespace Client.Core.ViewModels;

public abstract class ViewModelBase : INotifyPropertyChanged
{
  public event PropertyChangedEventHandler? PropertyChanged;

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
