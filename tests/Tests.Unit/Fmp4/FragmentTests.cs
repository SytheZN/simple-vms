using System.Buffers.Binary;
using System.Text;
using Format.Fmp4;

namespace Tests.Unit.Fmp4;

[TestFixture]
public class FragmentTests
{
  /// <summary>
  /// SCENARIO:
  /// Generate a moof box with known parameters
  ///
  /// ACTION:
  /// Build a moof with sequence=1, one keyframe sample
  ///
  /// EXPECTED RESULT:
  /// Output starts with moof box, contains mfhd with sequence_number=1
  /// </summary>
  [Test]
  public void Moof_ContainsMfhdWithSequenceNumber()
  {
    var samples = new List<SampleEntry>
    {
      new() { Duration = 3000, Size = 500, IsKeyframe = true, CompositionOffset = 0 }
    };

    var moof = MoofBuilder.Build(1, 0, samples);
    var type = Encoding.ASCII.GetString(moof, 4, 4);

    Assert.That(type, Is.EqualTo("moof"));

    var mfhdOffset = FindBox(moof, "mfhd");
    Assert.That(mfhdOffset, Is.GreaterThan(-1));

    var seqNum = BinaryPrimitives.ReadUInt32BigEndian(moof.AsSpan(mfhdOffset + 12));
    Assert.That(seqNum, Is.EqualTo(1u));
  }

  /// <summary>
  /// SCENARIO:
  /// moof contains tfdt with correct base decode time
  ///
  /// ACTION:
  /// Build a moof with baseDecodeTime=90000
  ///
  /// EXPECTED RESULT:
  /// tfdt contains version=1 and baseMediaDecodeTime=90000
  /// </summary>
  [Test]
  public void Moof_TfdtHasCorrectBaseDecodeTime()
  {
    var samples = new List<SampleEntry>
    {
      new() { Duration = 3000, Size = 100, IsKeyframe = false, CompositionOffset = 0 }
    };

    var moof = MoofBuilder.Build(1, 90000, samples);
    var tfdtOffset = FindBox(moof, "tfdt");
    Assert.That(tfdtOffset, Is.GreaterThan(-1));

    var version = moof[tfdtOffset + 8];
    Assert.That(version, Is.EqualTo(1));

    var baseTime = BinaryPrimitives.ReadUInt64BigEndian(moof.AsSpan(tfdtOffset + 12));
    Assert.That(baseTime, Is.EqualTo(90000UL));
  }

  /// <summary>
  /// SCENARIO:
  /// trun has correct sample count and keyframe flags
  ///
  /// ACTION:
  /// Build a moof with 1 keyframe sample
  ///
  /// EXPECTED RESULT:
  /// trun sample_count=1, sample flags indicate keyframe (0x02000000)
  /// </summary>
  [Test]
  public void Moof_TrunHasCorrectSampleCountAndFlags()
  {
    var samples = new List<SampleEntry>
    {
      new() { Duration = 3000, Size = 500, IsKeyframe = true, CompositionOffset = 0 }
    };

    var moof = MoofBuilder.Build(1, 0, samples);
    var trunOffset = FindBox(moof, "trun");
    Assert.That(trunOffset, Is.GreaterThan(-1));

    var sampleCount = BinaryPrimitives.ReadUInt32BigEndian(moof.AsSpan(trunOffset + 12));
    Assert.That(sampleCount, Is.EqualTo(1u));

    // trun layout: header(8) + version+flags(4) + sample_count(4) + data_offset(4)
    // + per-sample: duration(4) + size(4) + flags(4)
    var sampleFlags = BinaryPrimitives.ReadUInt32BigEndian(moof.AsSpan(trunOffset + 28));
    Assert.That(sampleFlags, Is.EqualTo(0x02000000u));
  }

  /// <summary>
  /// SCENARIO:
  /// mdat box contains length-prefixed NAL data
  ///
  /// ACTION:
  /// Build an mdat from two Annex B NAL units
  ///
  /// EXPECTED RESULT:
  /// mdat header is "mdat", payload has 4-byte length prefix per NAL
  /// </summary>
  [Test]
  public void Mdat_ContainsLengthPrefixedNals()
  {
    var nals = new List<ReadOnlyMemory<byte>>
    {
      new byte[] { 0x00, 0x00, 0x00, 0x01, 0x65, 0xAA },
      new byte[] { 0x00, 0x00, 0x00, 0x01, 0x41, 0xBB }
    };

    var mdat = MdatBuilder.Build(nals);
    var type = Encoding.ASCII.GetString(mdat, 4, 4);

    Assert.That(type, Is.EqualTo("mdat"));

    var nal1Len = BinaryPrimitives.ReadUInt32BigEndian(mdat.AsSpan(8));
    Assert.That(nal1Len, Is.EqualTo(2u));
    Assert.That(mdat[12], Is.EqualTo(0x65));
    Assert.That(mdat[13], Is.EqualTo(0xAA));

    var nal2Len = BinaryPrimitives.ReadUInt32BigEndian(mdat.AsSpan(14));
    Assert.That(nal2Len, Is.EqualTo(2u));
    Assert.That(mdat[18], Is.EqualTo(0x41));
    Assert.That(mdat[19], Is.EqualTo(0xBB));
  }

  /// <summary>
  /// SCENARIO:
  /// data_offset in trun points past moof into mdat payload
  ///
  /// ACTION:
  /// Build moof, check data_offset = moof_size + 8
  ///
  /// EXPECTED RESULT:
  /// data_offset equals the moof box size plus 8 (mdat header)
  /// </summary>
  [Test]
  public void Moof_DataOffsetPointsToMdatPayload()
  {
    var samples = new List<SampleEntry>
    {
      new() { Duration = 3000, Size = 100, IsKeyframe = true, CompositionOffset = 0 }
    };

    var moof = MoofBuilder.Build(1, 0, samples);
    var moofSize = BinaryPrimitives.ReadUInt32BigEndian(moof);

    var trunOffset = FindBox(moof, "trun");
    var dataOffset = BinaryPrimitives.ReadInt32BigEndian(moof.AsSpan(trunOffset + 16));

    Assert.That(dataOffset, Is.EqualTo((int)moofSize + 8));
  }

  /// <summary>
  /// SCENARIO:
  /// Fragment assembler tracks keyframe byte offsets
  ///
  /// ACTION:
  /// Assemble two fragments (keyframe, then non-keyframe)
  ///
  /// EXPECTED RESULT:
  /// First returns a KeyframeOffset, second returns null
  /// </summary>
  [Test]
  public void FragmentAssembler_TracksKeyframeOffsets()
  {
    var timestamps = new TimestampConverter(90000);
    timestamps.ToDecodeTime(0);
    var assembler = new FragmentAssembler(timestamps);

    var nals = new List<ReadOnlyMemory<byte>>
    {
      new byte[] { 0x00, 0x00, 0x00, 0x01, 0x65, 0xAA }
    };
    var samples = new List<SampleEntry>
    {
      new() { Duration = 3000, Size = 6, IsKeyframe = true, CompositionOffset = 0 }
    };

    var (frag1, kf1) = assembler.Assemble(nals, samples, 0, true);
    Assert.That(kf1, Is.Not.Null);
    Assert.That(kf1!.ByteOffset, Is.GreaterThanOrEqualTo(0));
    var moofType = System.Text.Encoding.ASCII.GetString(frag1.Data.Span.Slice((int)kf1.ByteOffset + 4, 4));
    Assert.That(moofType, Is.EqualTo("moof"));

    var (frag2, kf2) = assembler.Assemble(nals, samples, 33333, false);
    Assert.That(kf2, Is.Null);
    Assert.That(assembler.BytesWritten, Is.EqualTo(frag1.Data.Length + frag2.Data.Length));
  }

  /// <summary>
  /// SCENARIO:
  /// Sequence numbers increment across fragments
  ///
  /// ACTION:
  /// Build two moofs sequentially
  ///
  /// EXPECTED RESULT:
  /// First has sequence=1, second has sequence=2
  /// </summary>
  [Test]
  public void Moof_SequenceNumbersIncrement()
  {
    var samples = new List<SampleEntry>
    {
      new() { Duration = 3000, Size = 100, IsKeyframe = false, CompositionOffset = 0 }
    };

    var moof1 = MoofBuilder.Build(1, 0, samples);
    var moof2 = MoofBuilder.Build(2, 3000, samples);

    var mfhd1Offset = FindBox(moof1, "mfhd");
    var mfhd2Offset = FindBox(moof2, "mfhd");

    var seq1 = BinaryPrimitives.ReadUInt32BigEndian(moof1.AsSpan(mfhd1Offset + 12));
    var seq2 = BinaryPrimitives.ReadUInt32BigEndian(moof2.AsSpan(mfhd2Offset + 12));

    Assert.That(seq1, Is.EqualTo(1u));
    Assert.That(seq2, Is.EqualTo(2u));
  }

  private static int FindBox(byte[] data, string type)
  {
    var target = Encoding.ASCII.GetBytes(type);
    for (var i = 0; i <= data.Length - 8; i++)
    {
      if (data[i + 4] == target[0] && data[i + 5] == target[1]
        && data[i + 6] == target[2] && data[i + 7] == target[3])
        return i;
    }
    return -1;
  }
}
