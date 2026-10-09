namespace Shared.Models;

public static class EnumExtensions
{
  extension(Enum)
  {
    public static bool TryParseExact<TEnum>(string? value, out TEnum result) where TEnum : struct, Enum
    {
      foreach (var candidate in Enum.GetValues<TEnum>())
      {
        if (string.Equals(candidate.ToString(), value, StringComparison.OrdinalIgnoreCase))
        {
          result = candidate;
          return true;
        }
      }

      result = default;
      return false;
    }
  }
}
