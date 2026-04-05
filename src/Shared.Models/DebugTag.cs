namespace Shared.Models;

public readonly record struct DebugTag(uint Value)
{
  public ushort Module => (ushort)(Value >> 16);
  public ushort Code => (ushort)(Value & 0xFFFF);

  public DebugTag(ushort module, ushort code)
    : this((uint)(module << 16) | code) { }

  public override string ToString() => $"0x{Value:X8}";
}
