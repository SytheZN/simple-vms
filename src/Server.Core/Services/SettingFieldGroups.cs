using Shared.Models;

namespace Server.Core.Services;

public static class SettingFieldGroups
{
  public static IEnumerable<(string GroupId, IReadOnlyDictionary<string, string> Values)> Collect(
    IReadOnlyList<SettingGroup> schema,
    IReadOnlyDictionary<string, string> saved,
    IReadOnlyDictionary<string, string> submitted)
  {
    var grouped = schema
      .SelectMany(g => g.Fields)
      .Where(f => f.GroupId != null)
      .ToList();

    var touched = grouped
      .Where(f => submitted.ContainsKey(f.Key))
      .Select(f => f.GroupId!)
      .Distinct();

    foreach (var groupId in touched)
    {
      var values = new Dictionary<string, string>();
      foreach (var field in grouped.Where(f => f.GroupId == groupId))
      {
        if (submitted.TryGetValue(field.Key, out var value) || saved.TryGetValue(field.Key, out value))
          values[field.Key] = value;
      }
      yield return (groupId, values);
    }
  }
}
