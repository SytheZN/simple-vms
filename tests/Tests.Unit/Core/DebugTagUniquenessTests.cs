using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Client.Core;
using Shared.Models;

namespace Tests.Unit.Core;

[TestFixture]
public class DebugTagUniquenessTests
{
  private static readonly Regex TagSite = new(
    @"\b(ClientModuleIds|ModuleIds)\.(\w+)\s*,\s*0x([0-9A-Fa-f]+)",
    RegexOptions.Compiled);

  /// <summary>
  /// SCENARIO:
  /// Debug tags encode a globally unique {module id, specific code} pair.
  /// Module IDs share a single ushort namespace across client and server -
  /// ModuleIds and ClientModuleIds declare into the same space with disjoint
  /// values. The response-model contract requires that a given tag maps to
  /// exactly one return/throw site; otherwise a tag in logs or on the wire
  /// cannot identify where it originated.
  ///
  /// ACTION:
  /// Resolve every ModuleIds.* / ClientModuleIds.* reference in src/ to its
  /// numeric module id via reflection, then group by (moduleId, specificCode)
  /// and record source locations.
  ///
  /// EXPECTED RESULT:
  /// No (moduleId, specificCode) pair appears at more than one distinct
  /// source site. The failure message names each collision and the files
  /// and line numbers involved.
  /// </summary>
  [Test]
  public void SpecificCodes_AreUniquePerModule()
  {
    var moduleMap = BuildModuleIdMap();
    var repoRoot = FindRepoRoot();
    var srcDir = Path.Combine(repoRoot, "src");

    var zeroModules = moduleMap
      .Where(kv => kv.Value == 0)
      .Select(kv => kv.Key)
      .OrderBy(s => s)
      .ToList();
    Assert.That(zeroModules, Is.Empty,
      "Module IDs must be non-zero (0x0000 is reserved for untagged / unknown origin):\n  "
      + string.Join("\n  ", zeroModules));

    var sites = new Dictionary<(ushort ModuleId, int Code), HashSet<string>>();
    var unresolved = new HashSet<string>();
    var zeroCodeSites = new List<string>();

    foreach (var file in Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories))
    {
      if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
          || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
        continue;

      var text = File.ReadAllText(file);
      foreach (Match match in TagSite.Matches(text))
      {
        var qualifier = $"{match.Groups[1].Value}.{match.Groups[2].Value}";
        var code = int.Parse(
          match.Groups[3].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var line = CountNewlines(text, match.Index) + 1;
        var relative = Path.GetRelativePath(repoRoot, file);
        var location = $"{relative}:{line}";

        if (!moduleMap.TryGetValue(qualifier, out var moduleId))
        {
          unresolved.Add($"{qualifier} at {location}");
          continue;
        }

        if (code == 0)
        {
          zeroCodeSites.Add($"{qualifier} at {location}");
          continue;
        }

        var key = (moduleId, code);
        if (!sites.TryGetValue(key, out var locations))
          sites[key] = locations = [];
        locations.Add(location);
      }
    }

    Assert.That(unresolved, Is.Empty,
      "Unresolved module-id references (not declared in ModuleIds/ClientModuleIds):\n  "
      + string.Join("\n  ", unresolved.OrderBy(s => s)));

    Assert.That(zeroCodeSites, Is.Empty,
      "Specific codes must be non-zero (0x0000 is reserved for untagged / unknown origin):\n  "
      + string.Join("\n  ", zeroCodeSites.OrderBy(s => s)));

    var duplicates = sites
      .Where(kv => kv.Value.Count > 1)
      .OrderBy(kv => kv.Key.ModuleId).ThenBy(kv => kv.Key.Code)
      .Select(kv =>
        $"  0x{kv.Key.ModuleId:X4}/0x{kv.Key.Code:X4} at:\n    "
        + string.Join("\n    ", kv.Value.OrderBy(s => s)))
      .ToList();

    Assert.That(duplicates, Is.Empty,
      "Duplicate debug-tag specific codes found (response-model rule: one tag, one site):\n"
      + string.Join("\n", duplicates));
  }

  private static Dictionary<string, ushort> BuildModuleIdMap()
  {
    var map = new Dictionary<string, ushort>();
    foreach (var type in new[] { typeof(ModuleIds), typeof(ClientModuleIds) })
    {
      foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
      {
        if (!field.IsLiteral || field.FieldType != typeof(ushort)) continue;
        map[$"{type.Name}.{field.Name}"] = (ushort)field.GetValue(null)!;
      }
    }
    return map;
  }

  private static int CountNewlines(string text, int endExclusive)
  {
    var count = 0;
    for (var i = 0; i < endExclusive; i++)
      if (text[i] == '\n') count++;
    return count;
  }

  private static string FindRepoRoot()
  {
    var dir = AppContext.BaseDirectory;
    while (dir != null && !File.Exists(Path.Combine(dir, "build.sh")))
      dir = Path.GetDirectoryName(dir);
    if (dir == null)
      throw new InvalidOperationException(
        "Could not locate repo root (build.sh) from test base directory");
    return dir;
  }
}
