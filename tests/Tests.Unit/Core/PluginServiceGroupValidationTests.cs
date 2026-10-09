using System.Runtime.Loader;
using Server.Core.Services;
using Server.Plugins;
using Tests.Unit.Mocks;

namespace Tests.Unit.Core;

[TestFixture]
public class PluginServiceGroupValidationTests
{
  private string _dataPath = null!;

  [SetUp]
  public void SetUp()
  {
    _dataPath = Path.Combine(Path.GetTempPath(), $"plugin-service-{Guid.NewGuid():N}");
    Directory.CreateDirectory(_dataPath);
  }

  [TearDown]
  public void TearDown() => Directory.Delete(_dataPath, recursive: true);

  /// <summary>
  /// SCENARIO:
  /// A grouped field is checked while the form holds an unsaved change to the other field in
  /// its group, and the combination breaks the group rule
  ///
  /// ACTION:
  /// ValidateField(id, key, value, formValues)
  ///
  /// EXPECTED RESULT:
  /// Rejected; the group is checked against the form's values, not the saved ones
  /// </summary>
  [Test]
  public void ValidateField_GroupCheckedAgainstFormValues()
  {
    var plugin = new GroupedSettingsPlugin();
    var service = CreateService(plugin);

    var result = service.ValidateField(plugin.Metadata.Id, "value", "200",
      new Dictionary<string, string> { ["mode"] = "percent", ["value"] = "200" });

    Assert.That(result.IsT1, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// A grouped field is checked with no form values; the saved value of the other field in
  /// the group makes the combination valid
  ///
  /// ACTION:
  /// ValidateField(id, key, value)
  ///
  /// EXPECTED RESULT:
  /// Accepted; missing fields fall back to their saved values
  /// </summary>
  [Test]
  public void ValidateField_GroupFallsBackToSavedValues()
  {
    var plugin = new GroupedSettingsPlugin();
    var service = CreateService(plugin);

    var result = service.ValidateField(plugin.Metadata.Id, "value", "200");

    Assert.That(result.IsT0, Is.True);
    Assert.That(plugin.GroupCalls, Is.EqualTo(new[] { "pair" }));
  }

  /// <summary>
  /// SCENARIO:
  /// A field with no group is checked
  ///
  /// ACTION:
  /// ValidateField(id, key, value, formValues)
  ///
  /// EXPECTED RESULT:
  /// Only the per-field check runs; no group is validated
  /// </summary>
  [Test]
  public void ValidateField_UngroupedField_NoGroupCheck()
  {
    var plugin = new GroupedSettingsPlugin();
    var service = CreateService(plugin);

    var result = service.ValidateField(plugin.Metadata.Id, "loose", "x",
      new Dictionary<string, string> { ["mode"] = "percent", ["value"] = "200" });

    Assert.That(result.IsT0, Is.True);
    Assert.That(plugin.GroupCalls, Is.Empty);
  }

  /// <summary>
  /// SCENARIO:
  /// Plugin settings are saved with a combination that breaks a group rule
  ///
  /// ACTION:
  /// ApplyConfigValues(id, values)
  ///
  /// EXPECTED RESULT:
  /// Rejected before the plugin applies anything
  /// </summary>
  [Test]
  public void ApplyConfigValues_GroupRuleBroken_RejectedBeforeApply()
  {
    var plugin = new GroupedSettingsPlugin();
    var service = CreateService(plugin);

    var result = service.ApplyConfigValues(plugin.Metadata.Id,
      new Dictionary<string, string> { ["mode"] = "percent", ["value"] = "200" });

    Assert.That(result.IsT1, Is.True);
    Assert.That(plugin.Applied, Is.False);
  }

  /// <summary>
  /// SCENARIO:
  /// Plugin settings are saved with a valid combination for the group
  ///
  /// ACTION:
  /// ApplyConfigValues(id, values)
  ///
  /// EXPECTED RESULT:
  /// The group is checked and the plugin applies the values
  /// </summary>
  [Test]
  public void ApplyConfigValues_GroupRuleSatisfied_Applied()
  {
    var plugin = new GroupedSettingsPlugin();
    var service = CreateService(plugin);

    var result = service.ApplyConfigValues(plugin.Metadata.Id,
      new Dictionary<string, string> { ["mode"] = "percent", ["value"] = "50" });

    Assert.That(result.IsT0, Is.True);
    Assert.That(plugin.GroupCalls, Is.EqualTo(new[] { "pair" }));
    Assert.That(plugin.Applied, Is.True);
  }

  private PluginService CreateService(IPlugin plugin)
  {
    var host = new FakePluginHost
    {
      Plugins =
      [
        new PluginEntry
        {
          PluginType = plugin.GetType(),
          LoadContext = AssemblyLoadContext.Default,
          Plugin = plugin,
          Metadata = plugin.Metadata
        }
      ]
    };
    return new PluginService(host, new DataProviderConfigJsonStore(_dataPath));
  }

  private sealed class GroupedSettingsPlugin : IPlugin, IPluginSettings
  {
    public List<string> GroupCalls { get; } = [];

    public PluginMetadata Metadata { get; } = new()
    {
      Id = "grouped",
      Name = "Grouped",
      Version = "1.0.0",
      Description = ""
    };

    public OneOf<Success, Error> Initialize(PluginContext context) => new Success();
    public Task<OneOf<Success, Error>> StartAsync(CancellationToken ct) =>
      Task.FromResult<OneOf<Success, Error>>(new Success());
    public Task<OneOf<Success, Error>> StopAsync(CancellationToken ct) =>
      Task.FromResult<OneOf<Success, Error>>(new Success());

    public IReadOnlyList<SettingGroup> GetSchema() =>
    [
      new SettingGroup
      {
        Key = "section",
        Order = 0,
        Label = "Section",
        Fields =
        [
          new SettingField { Key = "mode", Order = 0, Label = "Mode", Type = "string", GroupId = "pair" },
          new SettingField { Key = "value", Order = 1, Label = "Value", Type = "string", GroupId = "pair" },
          new SettingField { Key = "loose", Order = 2, Label = "Loose", Type = "string" }
        ]
      }
    ];

    public IReadOnlyDictionary<string, string> GetValues() =>
      new Dictionary<string, string> { ["mode"] = "days", ["value"] = "30", ["loose"] = "" };

    public OneOf<Success, Error> ValidateValue(string key, string value) => new Success();

    public OneOf<Success, Error> ValidateGroup(string groupId, IReadOnlyDictionary<string, string> values)
    {
      GroupCalls.Add(groupId);
      return values["mode"] == "percent" && int.Parse(values["value"]) > 100
        ? Error.Create(0x1FFF, 0x0001, Result.BadRequest, "too large for percent")
        : new Success();
    }

    public bool Applied { get; private set; }

    public OneOf<Success, Error> ApplyValues(IReadOnlyDictionary<string, string> values)
    {
      Applied = true;
      return new Success();
    }
  }
}
