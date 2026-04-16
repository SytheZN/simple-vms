using System.Runtime.InteropServices;

namespace Client.Core.Decoding;

internal static class FfmpegLoader
{
  private static int _initialized;

  internal static void EnsureLoaded()
  {
    if (Interlocked.CompareExchange(ref _initialized, 1, 0) != 0)
      return;

    var dir = FindNativeDirectory();
    if (dir == null)
      return;

    // Load in dependency order so transitive deps resolve from our dir
    NativeLibrary.Load(Path.Combine(dir, MapName("avutil")));
    NativeLibrary.Load(Path.Combine(dir, MapName("swscale")));
    NativeLibrary.Load(Path.Combine(dir, MapName("avcodec")));
  }

  private static string? FindNativeDirectory()
  {
    var baseDir = AppContext.BaseDirectory;

    var rid = RuntimeInformation.RuntimeIdentifier;
    var candidate = Path.Combine(baseDir, "runtimes", rid, "native");
    if (Directory.Exists(candidate))
      return candidate;

    var os = OperatingSystem.IsLinux() ? "linux"
      : OperatingSystem.IsWindows() ? "win"
      : OperatingSystem.IsMacOS() ? "osx"
      : null;
    var arch = RuntimeInformation.ProcessArchitecture switch
    {
      Architecture.X64 => "x64",
      Architecture.Arm64 => "arm64",
      _ => null
    };
    if (os != null && arch != null)
    {
      candidate = Path.Combine(baseDir, "runtimes", $"{os}-{arch}", "native");
      if (Directory.Exists(candidate))
        return candidate;
    }

    return null;
  }

  private static string MapName(string lib)
  {
    if (OperatingSystem.IsWindows())
    {
      return lib switch
      {
        "avcodec" => "avcodec-62.dll",
        "avutil" => "avutil-60.dll",
        "swscale" => "swscale-9.dll",
        _ => lib
      };
    }
    if (OperatingSystem.IsMacOS())
      return $"lib{lib}.dylib";
    return $"lib{lib}.so";
  }
}
