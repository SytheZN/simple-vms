using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shared.Models;

[JsonConverter(typeof(DebugTagJsonConverter))]
public readonly record struct DebugTag(uint Value)
{
  public ushort Module => (ushort)(Value >> 16);
  public ushort Code => (ushort)(Value & 0xFFFF);

  public DebugTag(ushort module, ushort code)
    : this((uint)(module << 16) | code) { }

  public override string ToString() => $"0x{Value:X8}";
}

public sealed class DebugTagJsonConverter : JsonConverter<DebugTag>
{
  public override DebugTag Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
  {
    var str = reader.GetString();
    if (str == null || !str.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
      throw new JsonException($"Expected hex string with 0x prefix, got '{str}'");
    return new(uint.Parse(str.AsSpan(2), NumberStyles.HexNumber));
  }

  public override void Write(Utf8JsonWriter writer, DebugTag value, JsonSerializerOptions options) =>
    writer.WriteStringValue(value.ToString());
}
