namespace Client.Core.Decoding;

public interface IChunkDecoder<F> where F : IDecodedItem
{
  void Decode(ReadOnlyMemory<byte> data, ulong gopTimestamp);
  void Dispose(F frame);
}
