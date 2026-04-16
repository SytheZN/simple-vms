namespace Client.Core;

/// <summary>
/// Runs a fire-and-forget task and terminates the process if it throws.
/// This is used instead of raw Task.Run for background loops so exceptions
/// surface immediately rather than being held until task finalization
/// (which the default TaskScheduler.UnobservedTaskException only catches
/// at GC time).
/// </summary>
internal static class FailFast
{
  public static Task Run(Func<Task> work) =>
    Task.Run(async () =>
    {
      try { await work(); }
      catch (OperationCanceledException) { }
      catch (Exception ex)
      {
        Environment.FailFast("Background task crashed", ex);
      }
    });
}
