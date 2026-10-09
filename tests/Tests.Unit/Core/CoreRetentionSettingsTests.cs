using Server.Core.Services;
using Tests.Unit.Mocks;

namespace Tests.Unit.Core;

[TestFixture]
public class CoreRetentionSettingsTests
{
  /// <summary>
  /// SCENARIO:
  /// A camera or stream retention field is submitted with a numeric mode, an unknown mode,
  /// a non-numeric value, or a decimal value
  ///
  /// ACTION:
  /// ValidateValue(id, key, value) on camera and stream settings
  ///
  /// EXPECTED RESULT:
  /// Numeric and unknown modes and non-numeric values are rejected; named modes and decimal
  /// values are accepted
  /// </summary>
  [TestCase("retentionMode", "42", false)]
  [TestCase("retentionMode", "weeks", false)]
  [TestCase("retentionMode", "days", true)]
  [TestCase("retentionValue", "abc", false)]
  [TestCase("retentionValue", "1.5", true)]
  [TestCase("retentionValue", "", true)]
  public void ValidateValue_RetentionFields(string key, string value, bool expectValid)
  {
    var host = Host(freeBytes: 1000);

    Assert.That(new CoreCameraSettings(host).ValidateValue(Guid.NewGuid(), key, value).IsT0,
      Is.EqualTo(expectValid));
    Assert.That(new CoreStreamSettings(host).ValidateValue(Guid.NewGuid(), key, value).IsT0,
      Is.EqualTo(expectValid));
  }

  /// <summary>
  /// SCENARIO:
  /// A stream's retention group is submitted with combinations of mode, value and storage
  /// that can or cannot report its size
  ///
  /// ACTION:
  /// CoreStreamSettings.ValidateGroup(id, "retention", values)
  ///
  /// EXPECTED RESULT:
  /// A mode without a value is rejected; percent is rejected when the storage size is unknown;
  /// valid combinations are accepted
  /// </summary>
  [TestCase("days", "", 1000L, false)]
  [TestCase("days", "30", 1000L, true)]
  [TestCase("percent", "50", 1000L, true)]
  [TestCase("percent", "50", -1L, false)]
  [TestCase("default", "", -1L, true)]
  public void StreamValidateGroup_Retention(string mode, string value, long freeBytes, bool expectValid)
  {
    var settings = new CoreStreamSettings(Host(freeBytes));
    var values = new Dictionary<string, string>
    {
      [RetentionPolicyRules.ModeKey] = mode,
      [RetentionPolicyRules.ValueKey] = value
    };

    Assert.That(settings.ValidateGroup(Guid.NewGuid(), RetentionPolicyRules.GroupId, values).IsT0,
      Is.EqualTo(expectValid));
  }

  /// <summary>
  /// SCENARIO:
  /// A camera's retention group selects percent while the storage cannot report its size
  ///
  /// ACTION:
  /// CoreCameraSettings.ValidateGroup(id, "retention", values)
  ///
  /// EXPECTED RESULT:
  /// Rejected
  /// </summary>
  [Test]
  public void CameraValidateGroup_PercentWithUnknownStorage_Rejected()
  {
    var settings = new CoreCameraSettings(Host(freeBytes: -1));
    var values = new Dictionary<string, string>
    {
      [RetentionPolicyRules.ModeKey] = "percent",
      [RetentionPolicyRules.ValueKey] = "50"
    };

    Assert.That(settings.ValidateGroup(Guid.NewGuid(), RetentionPolicyRules.GroupId, values).IsT1, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// A group other than retention is validated on the core camera and stream settings
  ///
  /// ACTION:
  /// ValidateGroup(id, "other", values)
  ///
  /// EXPECTED RESULT:
  /// Accepted; core settings only define rules for the retention group
  /// </summary>
  [Test]
  public void ValidateGroup_OtherGroup_Accepted()
  {
    var host = Host(freeBytes: -1);
    var values = new Dictionary<string, string> { [RetentionPolicyRules.ModeKey] = "percent" };

    Assert.That(new CoreCameraSettings(host).ValidateGroup(Guid.NewGuid(), "other", values).IsT0, Is.True);
    Assert.That(new CoreStreamSettings(host).ValidateGroup(Guid.NewGuid(), "other", values).IsT0, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// A plugin implements the settings interfaces without providing ValidateGroup
  ///
  /// ACTION:
  /// Call ValidateGroup through each interface
  ///
  /// EXPECTED RESULT:
  /// The default implementation accepts every group, so existing plugins are unaffected
  /// </summary>
  [Test]
  public void ValidateGroup_DefaultImplementation_Accepts()
  {
    var plugin = new SettingsWithoutGroupRules();
    var values = new Dictionary<string, string> { ["any"] = "value" };

    Assert.That(((IPluginSettings)plugin).ValidateGroup("group", values).IsT0, Is.True);
    Assert.That(((IPluginCameraSettings)plugin).ValidateGroup(Guid.NewGuid(), "group", values).IsT0, Is.True);
    Assert.That(((IPluginStreamSettings)plugin).ValidateGroup(Guid.NewGuid(), "group", values).IsT0, Is.True);
  }

  private static FakePluginHost Host(long freeBytes) =>
    new() { StorageProviders = [new FreeSpaceStorage(freeBytes)] };

  private sealed class FreeSpaceStorage(long freeBytes) : IStorageProvider
  {
    public string ProviderId => "free-space";

    public Task<OneOf<long, Error>> GetFreeBytesAsync(CancellationToken ct) =>
      Task.FromResult<OneOf<long, Error>>(freeBytes);

    public Task<OneOf<ISegmentHandle, Error>> CreateSegmentAsync(SegmentMetadata metadata, CancellationToken ct) =>
      throw new NotImplementedException();
    public Task<OneOf<Stream, Error>> OpenReadAsync(string segmentRef, CancellationToken ct) =>
      throw new NotImplementedException();
    public Task<OneOf<Success, Error>> PurgeAsync(IReadOnlyList<string> segmentRefs, CancellationToken ct) =>
      throw new NotImplementedException();
    public Task<OneOf<StorageStats, Error>> GetStatsAsync(CancellationToken ct) =>
      throw new NotImplementedException();
  }

  private sealed class SettingsWithoutGroupRules : IPluginSettings, IPluginCameraSettings, IPluginStreamSettings
  {
    public IReadOnlyList<SettingGroup> GetSchema() => [];
    public IReadOnlyDictionary<string, string> GetValues() => new Dictionary<string, string>();
    public OneOf<Success, Error> ValidateValue(string key, string value) => new Success();
    public OneOf<Success, Error> ApplyValues(IReadOnlyDictionary<string, string> values) => new Success();

    public IReadOnlyList<SettingGroup> GetSchema(Guid id) => [];
    public IReadOnlyDictionary<string, string> GetValues(Guid id) => new Dictionary<string, string>();
    public OneOf<Success, Error> ValidateValue(Guid id, string key, string value) => new Success();
    public OneOf<Success, Error> ApplyValues(Guid id, IReadOnlyDictionary<string, string> values) => new Success();
    public Task<OneOf<Success, Error>> OnRemovedAsync(Guid id, CancellationToken ct) =>
      Task.FromResult<OneOf<Success, Error>>(new Success());
  }
}
