using System.Globalization;
using Shared.Models;

namespace Server.Core.Services;

public static class RetentionPolicyRules
{
  public const string GroupId = "retention";
  public const string ModeKey = "retentionMode";
  public const string ValueKey = "retentionValue";
  public const decimal MaxPercent = 100;
  public const decimal BytesPerGb = 1024m * 1024m * 1024m;
  private const decimal MaxGb = long.MaxValue / BytesPerGb;

  public static bool TryParseValue(string? value, out decimal parsed) =>
    decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed);

  public static string FormatValue(RetentionMode mode, long stored) =>
    stored == 0
      ? ""
      : DisplayValue(mode, stored).ToString("0.##", CultureInfo.InvariantCulture);

  public static decimal DisplayValue(RetentionMode mode, long stored) =>
    mode == RetentionMode.Bytes ? Math.Round(stored / BytesPerGb, 2) : stored;

  public static long StoredValue(RetentionMode mode, decimal display) =>
    mode == RetentionMode.Bytes ? (long)(display * BytesPerGb) : (long)display;

  public static long StoredValue(RetentionMode mode, string? display) =>
    TryParseValue(display, out var parsed) ? StoredValue(mode, parsed) : 0;

  public static bool StorageSizeKnown(IStorageProvider? storage)
  {
    if (storage == null)
      return false;

    var free = storage.GetFreeBytesAsync(CancellationToken.None).GetAwaiter().GetResult();
    return free.IsT0 && free.AsT0 >= 0;
  }

  public static OneOf<Success, Error> Validate(IReadOnlyDictionary<string, string> values, bool sizeKnown)
  {
    if (!Enum.TryParseExact<RetentionMode>(values.GetValueOrDefault(ModeKey), out var mode))
      return new Error(Result.BadRequest, new DebugTag(ModuleIds.Retention, 0x0001),
        "Mode must be one of default, days, bytes, percent");

    var value = TryParseValue(values.GetValueOrDefault(ValueKey), out var parsed) ? parsed : 0;
    return Validate(mode, value, sizeKnown);
  }

  public static OneOf<Success, Error> Validate(RetentionMode mode, decimal value, bool sizeKnown)
  {
    if (mode == RetentionMode.Percent && !sizeKnown)
      return new Error(Result.BadRequest, new DebugTag(ModuleIds.Retention, 0x0006),
        "Percent retention needs storage that can report its size");

    if (mode == RetentionMode.Default)
      return new Success();

    if (value <= 0)
      return new Error(Result.BadRequest, new DebugTag(ModuleIds.Retention, 0x0002),
        "Value must be greater than zero when retentionMode is set");

    if (mode != RetentionMode.Bytes && value != decimal.Truncate(value))
      return new Error(Result.BadRequest, new DebugTag(ModuleIds.Retention, 0x0004),
        "Value must be a whole number for days and percent");

    if (mode == RetentionMode.Percent && value > MaxPercent)
      return new Error(Result.BadRequest, new DebugTag(ModuleIds.Retention, 0x0003),
        $"Value must be at most {MaxPercent} for percent");

    if (value > (mode == RetentionMode.Bytes ? MaxGb : long.MaxValue))
      return new Error(Result.BadRequest, new DebugTag(ModuleIds.Retention, 0x0005),
        "Value is too large");

    return new Success();
  }
}
