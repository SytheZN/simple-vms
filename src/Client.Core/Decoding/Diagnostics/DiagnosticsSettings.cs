using System.ComponentModel;

namespace Client.Core.Decoding.Diagnostics;

public sealed class DiagnosticsSettings : INotifyPropertyChanged
{
  private bool _showOverlay;

  public event PropertyChangedEventHandler? PropertyChanged;

  public bool ShowOverlay
  {
    get => _showOverlay;
    set
    {
      if (_showOverlay == value) return;
      _showOverlay = value;
      PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowOverlay)));
    }
  }
}
