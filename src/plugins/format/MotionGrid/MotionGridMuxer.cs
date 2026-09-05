using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using Shared.Models;
using Shared.Models.Formats;

namespace Format.MotionGrid;

public sealed class MotionGridMuxer
{
  internal const int HeaderSize = 22;
  internal const byte Version = 1;
  internal const byte FlagSyncPoint = 0x01;
  internal static readonly byte[] Magic = [(byte)'M', (byte)'G', (byte)'R', (byte)'D'];

  private readonly IDataStream<MotionGridUnit> _input;
  private readonly string _fileExtension;
  private MotionGridUnit? _firstUnit;
  private byte[]? _prevFrame;
  private MuxStreamStatsMonitor? _statsMonitor;

  public Action<MuxStreamStats>? OnStats
  {
    set => _statsMonitor = value == null ? null : new MuxStreamStatsMonitor(value);
  }

  public MotionGridMuxer(IDataStream<MotionGridUnit> input, string fileExtension)
  {
    _input = input;
    _fileExtension = fileExtension;
  }

  public async Task<MuxStreamInfo> InitAsync(CancellationToken ct)
  {
    await foreach (var unit in _input.ReadAsync(ct))
    {
      _firstUnit = unit;
      return new MuxStreamInfo
      {
        DataFormat = "motion-grid",
        MimeType = "application/x-motion-grid",
        FileExtension = _fileExtension,
        Resolution = $"{unit.Width}x{unit.Height}",
        Fps = (int)Math.Round(_input.Info.Fps ?? 0m)
      };
    }
    throw new OperationCanceledException("Stream ended before first motion-grid unit");
  }

  public async IAsyncEnumerable<MotionGridFragment> MuxAsync(
    [EnumeratorCancellation] CancellationToken ct)
  {
    if (_firstUnit != null)
    {
      var first = _firstUnit;
      _firstUnit = null;
      var firstFragment = BuildFragment(first);
      _statsMonitor?.RecordFrame($"{first.Width}x{first.Height}", firstFragment.Data.Length);
      yield return firstFragment;
    }

    await foreach (var unit in _input.ReadAsync(ct))
    {
      var fragment = BuildFragment(unit);
      _statsMonitor?.RecordFrame($"{unit.Width}x{unit.Height}", fragment.Data.Length);
      yield return fragment;
    }
  }

  private MotionGridFragment BuildFragment(MotionGridUnit unit)
  {
    var cells = unit.Data.Span;
    var frameSize = cells.Length;

    var baseFrame = unit.IsSyncPoint ? null : _prevFrame;
    var delta = new byte[frameSize];
    for (var i = 0; i < frameSize; i++)
      delta[i] = (byte)(cells[i] ^ (baseFrame != null ? baseFrame[i] : 0));

    var payload = Deflate(delta);

    _prevFrame = cells.ToArray();

    var buffer = new byte[HeaderSize + payload.Length];
    var span = buffer.AsSpan();
    Magic.CopyTo(span);
    span[4] = Version;
    span[5] = unit.IsSyncPoint ? FlagSyncPoint : (byte)0;
    BinaryPrimitives.WriteUInt64LittleEndian(span[6..], unit.Timestamp);
    BinaryPrimitives.WriteUInt16LittleEndian(span[14..], unit.Width);
    BinaryPrimitives.WriteUInt16LittleEndian(span[16..], unit.Height);
    BinaryPrimitives.WriteUInt32LittleEndian(span[18..], (uint)payload.Length);
    payload.CopyTo(span[22..]);

    return new MotionGridFragment
    {
      Data = buffer,
      Timestamp = unit.Timestamp,
      IsSyncPoint = unit.IsSyncPoint
    };
  }

  private static byte[] Deflate(byte[] data)
  {
    using var output = new MemoryStream();
    using (var deflate = new DeflateStream(output, CompressionLevel.Fastest, leaveOpen: true))
      deflate.Write(data);
    return output.ToArray();
  }
}
