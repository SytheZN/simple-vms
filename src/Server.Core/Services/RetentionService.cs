using System.Globalization;
using Server.Plugins;
using Shared.Models;
using Shared.Api;

namespace Server.Core.Services;

public sealed class RetentionService
{
  public const string ModeKey = "retention.mode";
  public const string ValueKey = "retention.value";
  public const string MinFreeSpaceGbKey = "retention.minFreeSpaceGb";
  public const decimal MinFreeSpaceGbFloor = 0.5m;
  public const decimal MinFreeSpaceGbDefault = 2.0m;
  private const string SystemEventDaysKey = "retention.systemEventDays";
  private const int DefaultSystemEventDays = 180;

  private readonly IPluginHost _plugins;

  public RetentionService(IPluginHost plugins)
  {
    _plugins = plugins;
  }

  public async Task<OneOf<RetentionPolicy, Error>> GetGlobalAsync(CancellationToken ct)
  {
    var modeResult = await _plugins.DataProvider!.Config.GetAsync("server", ModeKey, ct);
    if (modeResult.IsT1) return modeResult.AsT1;

    var valueResult = await _plugins.DataProvider!.Config.GetAsync("server", ValueKey, ct);
    if (valueResult.IsT1) return valueResult.AsT1;

    var minFreeResult = await _plugins.DataProvider!.Config.GetAsync("server", MinFreeSpaceGbKey, ct);
    if (minFreeResult.IsT1) return minFreeResult.AsT1;

    return new RetentionPolicy
    {
      Mode = modeResult.AsT0 ?? "days",
      Value = long.TryParse(valueResult.AsT0, out var v) ? v : 30,
      MinFreeSpaceGb = ParseMinFreeSpaceGb(minFreeResult.AsT0)
    };
  }

  public async Task<OneOf<Success, Error>> SetGlobalAsync(
    RetentionPolicy policy, CancellationToken ct)
  {
    var modeResult = await _plugins.DataProvider!.Config.SetAsync("server", ModeKey, policy.Mode, ct);
    if (modeResult.IsT1) return modeResult.AsT1;

    var valueResult = await _plugins.DataProvider!.Config.SetAsync("server", ValueKey, policy.Value.ToString(), ct);
    if (valueResult.IsT1) return valueResult.AsT1;

    var minFree = policy.MinFreeSpaceGb < MinFreeSpaceGbFloor ? MinFreeSpaceGbFloor : policy.MinFreeSpaceGb;
    return await _plugins.DataProvider!.Config.SetAsync(
      "server", MinFreeSpaceGbKey, minFree.ToString(CultureInfo.InvariantCulture), ct);
  }

  public static decimal ParseMinFreeSpaceGb(string? stored)
  {
    if (stored != null
        && decimal.TryParse(stored, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
        && parsed >= MinFreeSpaceGbFloor)
      return parsed;
    return MinFreeSpaceGbDefault;
  }

  public async Task<OneOf<SystemEventRetentionDto, Error>> GetSystemEventRetentionAsync(
    CancellationToken ct)
  {
    var result = await _plugins.DataProvider!.Config.GetAsync("server", SystemEventDaysKey, ct);
    if (result.IsT1) return result.AsT1;

    return new SystemEventRetentionDto
    {
      Days = int.TryParse(result.AsT0, out var days) && days > 0 ? days : DefaultSystemEventDays
    };
  }

  public Task<OneOf<Success, Error>> SetSystemEventRetentionAsync(
    SystemEventRetentionDto retention, CancellationToken ct) =>
    _plugins.DataProvider!.Config.SetAsync(
      "server", SystemEventDaysKey, retention.Days.ToString(), ct);
}
