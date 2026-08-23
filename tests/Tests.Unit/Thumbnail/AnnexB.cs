namespace Tests.Unit.Thumbnail;

/// <summary>
/// Splits an Annex-B byte stream into NAL units. Start codes are three or four bytes and a stream
/// may mix them: ffmpeg writes four before parameter sets and three before slices.
/// </summary>
internal static class AnnexB
{
  public static List<byte[]> Split(byte[] stream)
  {
    var starts = new List<int>();
    for (var i = 0; i + 2 < stream.Length; i++)
      if (stream[i] == 0 && stream[i + 1] == 0 && stream[i + 2] == 1)
      {
        starts.Add(i + 3);
        i += 2;
      }

    var nals = new List<byte[]>();
    for (var i = 0; i < starts.Count; i++)
    {
      var end = i + 1 < starts.Count ? starts[i + 1] - 3 : stream.Length;
      while (end > starts[i] && stream[end - 1] == 0) end--;
      nals.Add(stream[starts[i]..end]);
    }
    return nals;
  }

  public static byte NalType(byte[] nal) => (byte)((nal[0] >> 1) & 0x3F);

  public static bool IsParameterSet(byte[] nal) => NalType(nal) is 32 or 33 or 34;
}
