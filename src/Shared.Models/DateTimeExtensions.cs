namespace Shared.Models;

public static class DateTimeExtensions
{
  public static ulong ToUnixMicroseconds(this DateTimeOffset value) =>
    (ulong)(value.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) / 10;

  public static ulong ToUnixMicroseconds(this DateTime value) =>
    (ulong)(value.ToUniversalTime().Ticks - DateTimeOffset.UnixEpoch.UtcTicks) / 10;
}
