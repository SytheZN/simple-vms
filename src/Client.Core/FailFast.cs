namespace Client.Core;

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
