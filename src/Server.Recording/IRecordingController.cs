namespace Server.Recording;

public interface IRecordingController
{
  bool IsHalted { get; }
  int WriterCount { get; }
  IReadOnlySet<Guid> ActiveSegmentIds { get; }
  Task HaltAllAsync();
  Task ResumeAsync(CancellationToken ct);
}
