using Shared.Models;
using Shared.Models.Dto;

namespace Server.Core.Services;

public sealed class RetentionService
{
  private const string ModeKey = "retention.mode";
  private const string ValueKey = "retention.value";

  private readonly IDataProvider _data;

  public RetentionService(IDataProvider data)
  {
    _data = data;
  }

  public async Task<OneOf<RetentionPolicy, Error>> GetGlobalAsync(CancellationToken ct)
  {
    var modeResult = await _data.Settings.GetAsync(ModeKey, ct);
    if (modeResult.IsT1) return modeResult.AsT1;

    var valueResult = await _data.Settings.GetAsync(ValueKey, ct);
    if (valueResult.IsT1) return valueResult.AsT1;

    return new RetentionPolicy
    {
      Mode = modeResult.AsT0 ?? "days",
      Value = long.TryParse(valueResult.AsT0, out var v) ? v : 30
    };
  }

  public async Task<OneOf<Success, Error>> SetGlobalAsync(
    RetentionPolicy policy, CancellationToken ct)
  {
    var modeResult = await _data.Settings.SetAsync(ModeKey, policy.Mode, ct);
    if (modeResult.IsT1) return modeResult.AsT1;

    return await _data.Settings.SetAsync(ValueKey, policy.Value.ToString(), ct);
  }
}
