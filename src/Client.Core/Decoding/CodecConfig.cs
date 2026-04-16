namespace Client.Core.Decoding;

public enum VideoCodec
{
  H264,
  H265,
  Mjpeg,
  MjpegB
}

public sealed record CodecParameters(
  VideoCodec Codec,
  int Width,
  int Height,
  byte[] Extradata);

public readonly record struct DemuxedSample(
  ReadOnlyMemory<byte> Data,
  long TimestampUs,
  long DecodeTimestampUs,
  long DurationUs,
  bool IsKey);
