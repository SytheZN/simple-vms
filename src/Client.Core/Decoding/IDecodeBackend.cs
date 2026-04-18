namespace Client.Core.Decoding;

public enum FrameKind { Cpu, Gpu }

/// <summary>Thread ownership is the caller's responsibility.</summary>
public interface IDecodeBackend : IDisposable
{
  FrameKind Kind { get; }
  string DisplayName { get; }
  bool Configure(CodecParameters config);
  bool SendSample(DemuxedSample sample);
  bool TryReceiveFrame(out DecodedFrame? frame);
  void Flush();
}
