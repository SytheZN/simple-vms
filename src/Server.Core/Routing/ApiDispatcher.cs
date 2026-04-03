using Shared.Models;

namespace Server.Core.Routing;

public sealed class ApiDispatcher
{
  public delegate Task<ResponseEnvelope> Handler(ApiRequest request, CancellationToken ct);

  private readonly List<Route> _routes = [];

  public void Add(string method, string pattern, Handler handler)
  {
    var segments = pattern.Split('/', StringSplitOptions.RemoveEmptyEntries);
    var paramIndices = new Dictionary<int, string>();
    var paramConstraints = new Dictionary<int, ParamConstraint>();

    for (var i = 0; i < segments.Length; i++)
    {
      if (!segments[i].StartsWith('{') || !segments[i].EndsWith('}'))
        continue;

      var inner = segments[i][1..^1];
      var colonIdx = inner.IndexOf(':');
      if (colonIdx >= 0)
      {
        var name = inner[..colonIdx];
        var constraint = inner[(colonIdx + 1)..];
        paramIndices[i] = name;
        if (string.Equals(constraint, "guid", StringComparison.OrdinalIgnoreCase))
          paramConstraints[i] = ParamConstraint.Guid;
        segments[i] = $"{{{name}}}";
      }
      else
      {
        paramIndices[i] = inner;
      }
    }

    _routes.Add(new Route(method, segments, paramIndices, paramConstraints, handler));
  }

  public Task<ResponseEnvelope>? TryDispatch(ApiRequest request, CancellationToken ct)
  {
    var path = request.Path;
    var queryStart = path.IndexOf('?');
    if (queryStart >= 0)
    {
      ParseQueryString(path[(queryStart + 1)..], request.Query);
      path = path[..queryStart];
    }

    var pathSegments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

    foreach (var route in _routes)
    {
      if (!string.Equals(route.Method, request.Method, StringComparison.OrdinalIgnoreCase))
        continue;

      if (!TryMatch(route, pathSegments, out var routeValues))
        continue;

      request.RouteValues = routeValues;
      return route.Handler(request, ct);
    }

    return null;
  }

  private static bool TryMatch(
    Route route, string[] pathSegments, out Dictionary<string, string> routeValues)
  {
    if (route.Segments.Length != pathSegments.Length)
    {
      routeValues = [];
      return false;
    }

    routeValues = [];

    for (var i = 0; i < route.Segments.Length; i++)
    {
      if (route.ParamIndices.TryGetValue(i, out var paramName))
      {
        if (route.ParamConstraints.TryGetValue(i, out var constraint)
            && constraint == ParamConstraint.Guid
            && !Guid.TryParse(pathSegments[i], out _))
          return false;
        routeValues[paramName] = pathSegments[i];
      }
      else if (!string.Equals(route.Segments[i], pathSegments[i], StringComparison.OrdinalIgnoreCase))
      {
        return false;
      }
    }

    return true;
  }

  private static void ParseQueryString(string queryString, Dictionary<string, string> target)
  {
    foreach (var pair in queryString.Split('&', StringSplitOptions.RemoveEmptyEntries))
    {
      var eq = pair.IndexOf('=');
      if (eq < 0) continue;
      var key = Uri.UnescapeDataString(pair[..eq]);
      var value = Uri.UnescapeDataString(pair[(eq + 1)..]);
      target.TryAdd(key, value);
    }
  }

  private enum ParamConstraint { Guid }

  private sealed record Route(
    string Method,
    string[] Segments,
    Dictionary<int, string> ParamIndices,
    Dictionary<int, ParamConstraint> ParamConstraints,
    Handler Handler);
}
