using Server.Core.Services;

namespace Tests.Unit.Core;

[TestFixture]
public class SettingFieldGroupsTests
{
  private static readonly IReadOnlyList<SettingGroup> Schema =
  [
    new SettingGroup
    {
      Key = "section",
      Order = 0,
      Label = "Section",
      Fields =
      [
        Field("mode", "pair"),
        Field("value", "pair"),
        Field("other", "single"),
        Field("loose", null)
      ]
    }
  ];

  /// <summary>
  /// SCENARIO:
  /// A request touches one field of a grouped pair; another group and an ungrouped field are
  /// not in the request
  ///
  /// ACTION:
  /// Collect(schema, saved, submitted)
  ///
  /// EXPECTED RESULT:
  /// Only the touched group is collected
  /// </summary>
  [Test]
  public void Collect_OnlyTouchedGroups()
  {
    var saved = new Dictionary<string, string> { ["mode"] = "a", ["value"] = "1", ["other"] = "x" };
    var submitted = new Dictionary<string, string> { ["mode"] = "b" };

    var groups = SettingFieldGroups.Collect(Schema, saved, submitted).ToList();

    Assert.That(groups.Select(g => g.GroupId), Is.EqualTo(new[] { "pair" }));
  }

  /// <summary>
  /// SCENARIO:
  /// A request submits one field of a grouped pair; the other field only has a saved value
  ///
  /// ACTION:
  /// Collect(schema, saved, submitted)
  ///
  /// EXPECTED RESULT:
  /// The group holds the submitted value over the saved one, and the saved value for the
  /// field that was not submitted
  /// </summary>
  [Test]
  public void Collect_MergesSubmittedOverSaved()
  {
    var saved = new Dictionary<string, string> { ["mode"] = "a", ["value"] = "1" };
    var submitted = new Dictionary<string, string> { ["mode"] = "b" };

    var (_, values) = SettingFieldGroups.Collect(Schema, saved, submitted).Single();

    Assert.That(values, Is.EqualTo(new Dictionary<string, string> { ["mode"] = "b", ["value"] = "1" }));
  }

  /// <summary>
  /// SCENARIO:
  /// A request submits only a field that has no group
  ///
  /// ACTION:
  /// Collect(schema, saved, submitted)
  ///
  /// EXPECTED RESULT:
  /// Nothing is collected; ungrouped settings keep per-field validation only
  /// </summary>
  [Test]
  public void Collect_UngroupedFieldsIgnored()
  {
    var submitted = new Dictionary<string, string> { ["loose"] = "z" };

    var groups = SettingFieldGroups.Collect(Schema, new Dictionary<string, string>(), submitted);

    Assert.That(groups, Is.Empty);
  }

  private static SettingField Field(string key, string? groupId) => new()
  {
    Key = key,
    Order = 0,
    Label = key,
    Type = "string",
    GroupId = groupId
  };
}
