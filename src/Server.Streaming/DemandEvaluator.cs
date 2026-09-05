namespace Server.Streaming;

public sealed class DemandEvaluator
{
  private readonly Func<Task> _evaluateOnce;
  private readonly Action<Exception> _onError;
  private readonly Lock _lock = new();
  private bool _running;
  private bool _again;

  public DemandEvaluator(Func<Task> evaluateOnce, Action<Exception> onError)
  {
    _evaluateOnce = evaluateOnce;
    _onError = onError;
  }

  public void Schedule()
  {
    lock (_lock)
    {
      if (_running)
      {
        _again = true;
        return;
      }
      _running = true;
    }
    _ = Task.Run(RunAsync);
  }

  private async Task RunAsync()
  {
    while (true)
    {
      try { await _evaluateOnce(); }
      catch (Exception ex) { _onError(ex); }

      lock (_lock)
      {
        if (!_again)
        {
          _running = false;
          return;
        }
        _again = false;
      }
    }
  }
}
