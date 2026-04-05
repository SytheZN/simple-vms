using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Server.Core.Routing;

public sealed class ApiRequest
{
  public required string Method { get; init; }
  public required string Path { get; init; }
  public Dictionary<string, string> RouteValues { get; set; } = [];
  public Dictionary<string, string> Query { get; init; } = [];
  public byte[]? BodyBytes { get; init; }
  public required IServiceProvider Services { get; init; }

  public T Body<T>(JsonTypeInfo<T> typeInfo) where T : class
  {
    if (BodyBytes is not { Length: > 0 })
      throw new InvalidOperationException($"No body provided for {typeof(T).Name}");
    return JsonSerializer.Deserialize(BodyBytes, typeInfo)
      ?? throw new InvalidOperationException($"Deserialization returned null for {typeof(T).Name}");
  }

  public T Resolve<T>() where T : notnull =>
    (T?)Services.GetService(typeof(T))
    ?? throw new InvalidOperationException($"Service not registered: {typeof(T).Name}");

  public Guid RouteGuid(string name) =>
    Guid.Parse(RouteValues[name]);

  public string RouteString(string name) =>
    RouteValues[name];

  public string? QueryString(string name) =>
    Query.GetValueOrDefault(name);

  public ulong QueryULong(string name) =>
    ulong.Parse(Query[name], CultureInfo.InvariantCulture);

  public ulong QueryULongOrDefault(string name, ulong defaultValue) =>
    Query.TryGetValue(name, out var v) ? ulong.Parse(v, CultureInfo.InvariantCulture) : defaultValue;

  public int QueryIntOrDefault(string name, int defaultValue) =>
    Query.TryGetValue(name, out var v) ? int.Parse(v, CultureInfo.InvariantCulture) : defaultValue;

  public int? QueryIntOrNull(string name) =>
    Query.TryGetValue(name, out var v) ? int.Parse(v, CultureInfo.InvariantCulture) : null;

  public Guid? QueryGuidOrNull(string name) =>
    Query.TryGetValue(name, out var v) ? Guid.Parse(v) : null;
}
