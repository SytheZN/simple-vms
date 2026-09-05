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
  private MuxStreamStatsMonitor? _statsMonitor;

  public Action<MuxStreamStats>? OnStats
  {
    set => _statsMonitor = value == null ? null : new MuxStreamStatsMonitor(value);
  }

  public MjpegMuxer(IDataStream<JpegUnit> input, string fileExtension)
  {
    _input = input;
    _fileExtension = fileExtension;
  }

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
    {
      var fragment = BuildFragment(unit);
      _statsMonitor?.RecordFrame($"{unit.Width}x{unit.Height}", fragment.Data.Length);
      yield return fragment;
    }
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
