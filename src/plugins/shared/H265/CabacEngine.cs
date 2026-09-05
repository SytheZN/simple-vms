namespace H265;

public sealed class CabacEngine : H264.CabacEngine
{
  public CabacEngine() : base(CabacContextInitTables.CtxCount) { }

  protected override void InitContexts(int sliceQp, H264.CabacInitType initType)
  {
    var init = CabacContextInitTables.InitValue[(int)initType];

    for (var i = 0; i < CabacContextInitTables.CtxCount; i++)
    {
      var m = (init[i] >> 4) * 5 - 45;
      var n = ((init[i] & 15) << 3) - 16;
      SetContext(i, m, n, sliceQp);
    }
  }
}
