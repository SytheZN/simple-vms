namespace Server.Recording;

public interface IRecordingController
{
  bool IsHalted { get; }
  int WriterCount { get; }
  Task HaltAllAsync();
  Task ResumeAsync(CancellationToken ct);
}
