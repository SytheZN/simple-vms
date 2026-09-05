namespace Client.Core.Decoding;

public enum FrameKind { Cpu, Gpu }

public interface IDecodeBackend : IDisposable
{
  FrameKind Kind { get; }
  string DisplayName { get; }
  bool Configure(CodecParameters config);
  bool SendSample(DemuxedSample sample);
  bool TryReceiveFrame(out DecodedFrame? frame);
  void Flush();
}
