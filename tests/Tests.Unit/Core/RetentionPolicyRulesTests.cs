using Server.Core.Services;
using Shared.Api;
using Tests.Unit.Mocks;

namespace Tests.Unit.Core;

[TestFixture]
public class RetentionPolicyRulesTests
{
  /// <summary>
  /// SCENARIO:
  /// A retention mode arrives as a numeric string, a flags-style combination, or an unknown name
  ///
  /// ACTION:
  /// Enum.TryParseExact&lt;RetentionMode&gt;(value)
  ///
  /// EXPECTED RESULT:
  /// Rejected; only the named modes parse
  /// </summary>
  [TestCase("42", false)]
  [TestCase("1", false)]
  [TestCase("days, bytes", false)]
  [TestCase("", false)]
  [TestCase("weeks", false)]
  [TestCase("default", true)]
  [TestCase("Days", true)]
  [TestCase("bytes", true)]
  [TestCase("PERCENT", true)]
  public void TryParseExact_AcceptsOnlyNamedModes(string value, bool expected)
  {
    Assert.That(Enum.TryParseExact<RetentionMode>(value, out _), Is.EqualTo(expected));
  }

  /// <summary>
  /// SCENARIO:
  /// A retention group has a non-default mode and a missing, blank, zero or negative value
  ///
  /// ACTION:
  /// Validate(values)
  ///
  /// EXPECTED RESULT:
  /// Rejected
  /// </summary>
  [TestCase("days", null)]
  [TestCase("days", "")]
  [TestCase("bytes", "0")]
  [TestCase("percent", "-5")]
  public void Validate_ModeWithoutValue_Rejected(string mode, string? value)
  {
    var values = new Dictionary<string, string> { [RetentionPolicyRules.ModeKey] = mode };
    if (value != null)
      values[RetentionPolicyRules.ValueKey] = value;

    Assert.That(RetentionPolicyRules.Validate(values, sizeKnown: true).IsT1, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// A retention group has a numeric or unknown mode with a valid value
  ///
  /// ACTION:
  /// Validate(values)
  ///
  /// EXPECTED RESULT:
  /// Rejected; the group check does not rely on the per-field mode check having run
  /// </summary>
  [TestCase("42")]
  [TestCase("weeks")]
  public void Validate_InvalidMode_Rejected(string mode)
  {
    var values = new Dictionary<string, string>
    {
      [RetentionPolicyRules.ModeKey] = mode,
      [RetentionPolicyRules.ValueKey] = "30"
    };

    Assert.That(RetentionPolicyRules.Validate(values, sizeKnown: true).IsT1, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// A retention group has the default mode with no value, a blank value, or a stale value
  ///
  /// ACTION:
  /// Validate(values)
  ///
  /// EXPECTED RESULT:
  /// Accepted; the value is ignored and the parent policy applies
  /// </summary>
  [TestCase(null)]
  [TestCase("")]
  [TestCase("30")]
  public void Validate_DefaultMode_IgnoresValue(string? value)
  {
    var values = new Dictionary<string, string> { [RetentionPolicyRules.ModeKey] = "default" };
    if (value != null)
      values[RetentionPolicyRules.ValueKey] = value;

    Assert.That(RetentionPolicyRules.Validate(values, sizeKnown: true).IsT0, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// A percent policy has a value at or beyond the 100% limit
  ///
  /// ACTION:
  /// Validate(Percent, value)
  ///
  /// EXPECTED RESULT:
  /// 100 is accepted; anything above is rejected
  /// </summary>
  [TestCase(100, true)]
  [TestCase(101, false)]
  public void Validate_PercentAbove100_Rejected(decimal value, bool expectValid)
  {
    Assert.That(RetentionPolicyRules.Validate(RetentionMode.Percent, value, sizeKnown: true).IsT0, Is.EqualTo(expectValid));
  }

  /// <summary>
  /// SCENARIO:
  /// The active storage cannot report its size
  ///
  /// ACTION:
  /// Validate(mode, value, sizeKnown: false)
  ///
  /// EXPECTED RESULT:
  /// Percent is rejected; days and size do not depend on the storage size and are accepted
  /// </summary>
  [TestCase(RetentionMode.Percent, false)]
  [TestCase(RetentionMode.Days, true)]
  [TestCase(RetentionMode.Bytes, true)]
  public void Validate_UnknownStorageSize_RejectsPercentOnly(RetentionMode mode, bool expectValid)
  {
    Assert.That(RetentionPolicyRules.Validate(mode, 30, sizeKnown: false).IsT0, Is.EqualTo(expectValid));
  }

  /// <summary>
  /// SCENARIO:
  /// A retention group has a fractional value for each mode
  ///
  /// ACTION:
  /// Validate(values)
  ///
  /// EXPECTED RESULT:
  /// Size accepts decimal GB; days and percent must be whole numbers
  /// </summary>
  [TestCase("bytes", "1.5", true)]
  [TestCase("days", "1.5", false)]
  [TestCase("percent", "11.25", false)]
  [TestCase("days", "30.0", true)]
  public void Validate_FractionalValues(string mode, string value, bool expectValid)
  {
    var values = new Dictionary<string, string>
    {
      [RetentionPolicyRules.ModeKey] = mode,
      [RetentionPolicyRules.ValueKey] = value
    };

    Assert.That(RetentionPolicyRules.Validate(values, sizeKnown: true).IsT0, Is.EqualTo(expectValid));
  }

  /// <summary>
  /// SCENARIO:
  /// A value is too large to store as a byte count or a whole number
  ///
  /// ACTION:
  /// Validate(mode, value)
  ///
  /// EXPECTED RESULT:
  /// Rejected rather than overflowing when converted for storage
  /// </summary>
  [TestCase(RetentionMode.Bytes, "9000000000")]
  [TestCase(RetentionMode.Days, "9223372036854775808")]
  public void Validate_TooLarge_Rejected(RetentionMode mode, string value)
  {
    Assert.That(RetentionPolicyRules.Validate(mode, decimal.Parse(value), sizeKnown: true).IsT1, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// A value entered in the settings form is stored and then read back for display
  ///
  /// ACTION:
  /// StoredValue(mode, display), then FormatValue(mode, stored)
  ///
  /// EXPECTED RESULT:
  /// Size is stored as bytes and shown as GB; days and percent are stored as entered;
  /// an empty value stores zero and shows blank
  /// </summary>
  [TestCase(RetentionMode.Bytes, "1.5", 1_610_612_736L, "1.5")]
  [TestCase(RetentionMode.Bytes, "2", 2_147_483_648L, "2")]
  [TestCase(RetentionMode.Days, "30", 30L, "30")]
  [TestCase(RetentionMode.Percent, "50", 50L, "50")]
  [TestCase(RetentionMode.Days, "", 0L, "")]
  public void StoredValue_RoundTripsThroughDisplay(
    RetentionMode mode, string display, long expectedStored, string expectedDisplay)
  {
    var stored = RetentionPolicyRules.StoredValue(mode, display);

    Assert.That(stored, Is.EqualTo(expectedStored));
    Assert.That(RetentionPolicyRules.FormatValue(mode, stored), Is.EqualTo(expectedDisplay));
  }

  /// <summary>
  /// SCENARIO:
  /// The server-wide policy is submitted with a minimum free space below the 0.5 GB floor
  ///
  /// ACTION:
  /// RetentionService.SetGlobalAsync(policy)
  ///
  /// EXPECTED RESULT:
  /// Rejected before anything is written
  /// </summary>
  [Test]
  public async Task SetGlobal_MinFreeSpaceBelowFloor_Rejected()
  {
    var service = new RetentionService(new FakePluginHost());

    var result = await service.SetGlobalAsync(
      new RetentionPolicy { Mode = "days", Value = 30, MinFreeSpaceGb = 0.4m }, CancellationToken.None);

    Assert.That(result.IsT1, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// The server-wide policy is submitted with the default mode, an unknown mode, or a numeric mode
  ///
  /// ACTION:
  /// RetentionService.SetGlobalAsync(policy)
  ///
  /// EXPECTED RESULT:
  /// Rejected before anything is written; the server-wide policy has no parent to inherit from
  /// </summary>
  [TestCase("default")]
  [TestCase("weeks")]
  [TestCase("2")]
  public async Task SetGlobal_InvalidMode_Rejected(string mode)
  {
    var service = new RetentionService(new FakePluginHost());

    var result = await service.SetGlobalAsync(
      new RetentionPolicy { Mode = mode, Value = 30, MinFreeSpaceGb = 2 }, CancellationToken.None);

    Assert.That(result.IsT1, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// The server-wide policy is submitted with a valid mode but a zero value
  ///
  /// ACTION:
  /// RetentionService.SetGlobalAsync(policy)
  ///
  /// EXPECTED RESULT:
  /// Rejected before anything is written
  /// </summary>
  [Test]
  public async Task SetGlobal_ZeroValue_Rejected()
  {
    var service = new RetentionService(new FakePluginHost());

    var result = await service.SetGlobalAsync(
      new RetentionPolicy { Mode = "days", Value = 0, MinFreeSpaceGb = 2 }, CancellationToken.None);

    Assert.That(result.IsT1, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// System event retention is submitted with zero or negative days
  ///
  /// ACTION:
  /// RetentionService.SetSystemEventRetentionAsync(retention)
  ///
  /// EXPECTED RESULT:
  /// Rejected before anything is written
  /// </summary>
  [TestCase(0)]
  [TestCase(-1)]
  public async Task SetSystemEventRetention_NonPositiveDays_Rejected(int days)
  {
    var service = new RetentionService(new FakePluginHost());

    var result = await service.SetSystemEventRetentionAsync(
      new SystemEventRetentionDto { Days = days }, CancellationToken.None);

    Assert.That(result.IsT1, Is.True);
  }
}
