namespace Server.Core;

public static class HostPort
{
  public static string SplitHost(string value)
  {
    if (value.StartsWith('['))
    {
      var close = value.IndexOf(']');
      return close > 0 ? value[1..close] : value;
    }
    if (value.IndexOf(':') != value.LastIndexOf(':')) return value;
    var colon = value.LastIndexOf(':');
    return colon >= 0 ? value[..colon] : value;
  }

  public static bool HasExplicitPort(string value)
  {
    if (value.StartsWith('['))
    {
      var close = value.IndexOf(']');
      return close >= 0 && close + 1 < value.Length && value[close + 1] == ':';
    }
    var first = value.IndexOf(':');
    return first >= 0 && first == value.LastIndexOf(':');
  }

  public static string NormalizeEndpoint(string value, int defaultPort)
  {
    if (HasExplicitPort(value)) return value;
    if (value.StartsWith('[')) return $"{value}:{defaultPort}";
    if (value.IndexOf(':') != value.LastIndexOf(':')) return $"[{value}]:{defaultPort}";
    return $"{value}:{defaultPort}";
  }
}
