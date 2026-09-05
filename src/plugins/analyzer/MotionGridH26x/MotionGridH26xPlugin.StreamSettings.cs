using Microsoft.Extensions.Logging;
using Shared.Models;

namespace Analyzer.MotionGridH26x;

public sealed partial class MotionGridH26xPlugin : IPluginStreamSettings
{
  private const string StreamEnabledKey = "streamEnabled";

  internal bool StreamEnabled(Guid streamId) =>
    _config.Get(StreamKey(streamId), "false") == "true";

  public IReadOnlyList<SettingGroup> GetSchema(Guid streamId) =>
    IsSupportedStream(streamId)
      ? [
        new SettingGroup
        {
          Key = GroupKey,
          Order = 0,
          Label = GroupLabel,
          Fields =
          [
            new SettingField
            {
              Key = StreamEnabledKey,
              Order = 0,
              Label = "Generate Motion Grid",
              Description = "Enable or disable motion grid generation for this stream.",
              Type = "boolean",
              DefaultValue = "false",
              Required = true
            }
          ]
        }
      ]
      : [];

  public IReadOnlyDictionary<string, string> GetValues(Guid streamId) =>
    new Dictionary<string, string>
    {
      [StreamEnabledKey] = _config.Get(StreamKey(streamId), "false")
    };

  public OneOf<Success, Error> ValidateValue(Guid streamId, string key, string value)
  {
    if (key == StreamEnabledKey && value != "true" && value != "false")
      return Error.Create(ModuleIds.PluginManagement, 0x0062, Result.BadRequest,
        $"{StreamEnabledKey} must be 'true' or 'false'");
    return new Success();
  }

  public OneOf<Success, Error> ApplyValues(Guid streamId, IReadOnlyDictionary<string, string> values)
  {
    foreach (var (key, value) in values)
    {
      var validated = ValidateValue(streamId, key, value);
      if (validated.IsT1) return validated.AsT1;
    }

    if (values.TryGetValue(StreamEnabledKey, out var enabled))
      _config.Set(StreamKey(streamId), enabled);

    return new Success();
  }

  public Task<OneOf<Success, Error>> OnRemovedAsync(Guid streamId, CancellationToken ct)
  {
    _config.Set(StreamKey(streamId), "");
    return Task.FromResult<OneOf<Success, Error>>(new Success());
  }

  private static string StreamKey(Guid streamId) =>
    $"stream/{streamId}/{StreamEnabledKey}";

  private bool IsSupportedStream(Guid streamId)
  {
    var result = _cameraRegistry.GetCamerasAsync(CancellationToken.None)
      .GetAwaiter().GetResult();
    return result.Match(
      cameras => cameras.SelectMany(c => c.Streams).Any(s =>
        s.Id == streamId && IsSupported(s)),
      error =>
      {
        _logger.LogWarning("Failed to load cameras while checking applicability ({Tag}): {Message}",
          error.Tag, error.Message);
        return false;
      });
  }
}
