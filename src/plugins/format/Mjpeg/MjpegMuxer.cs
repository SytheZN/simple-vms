using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Shared.Models;
using Shared.Models.Formats;

namespace Format.Mjpeg;

public sealed class MjpegMuxer
{
  internal const int HeaderSize = 17;
  internal const byte Version = 1;
  internal static readonly byte[] Magic = [(byte)'M', (byte)'J', (byte)'P', (byte)'G'];

  private readonly IDataStream<JpegUnit> _input;
  private readonly string _fileExtension;

  public MjpegMuxer(IDataStream<JpegUnit> input, string fileExtension)
  {
    _input = input;
    _fileExtension = fileExtension;
  }

  /// <summary>
  /// Resolution is left unset rather than read off a first frame: every JPEG carries its own
  /// dimensions, and blocking here would tie pipeline construction to the producer managing to
  /// emit, which for an analyzer-fed stream may never happen.
  /// </summary>
  public MuxStreamInfo Init() =>
    new()
    {
      DataFormat = "mjpeg",
      MimeType = "image/jpeg",
      FileExtension = _fileExtension,
      Resolution = "",
      Fps = (int)Math.Round(_input.Info.Fps ?? 0m)
    };

  public async IAsyncEnumerable<JpegFragment> MuxAsync(
    [EnumeratorCancellation] CancellationToken ct)
  {
    await foreach (var unit in _input.ReadAsync(ct))
      yield return BuildFragment(unit);
  }

  private static JpegFragment BuildFragment(JpegUnit unit)
  {
    var payload = unit.Data.Span;
    var buffer = new byte[HeaderSize + payload.Length];
    var span = buffer.AsSpan();

    Magic.CopyTo(span);
    span[4] = Version;
    BinaryPrimitives.WriteUInt64LittleEndian(span[5..], unit.Timestamp);
    BinaryPrimitives.WriteUInt32LittleEndian(span[13..], (uint)payload.Length);
    payload.CopyTo(span[HeaderSize..]);

    return new JpegFragment
    {
      Data = buffer,
      Timestamp = unit.Timestamp
    };
  }
}
