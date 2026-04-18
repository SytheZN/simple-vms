using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;
using SimpleVms.FFmpeg.Native;

namespace Client.Core.Decoding;

internal static class FfmpegLoader
{
  private static int _initialized;
  private static readonly Dictionary<string, IntPtr> _loaded = new();

  internal static void EnsureLoaded()
  {
    if (Interlocked.CompareExchange(ref _initialized, 1, 0) != 0)
      return;

    var dir = FindNativeDirectory();
    if (dir == null)
      return;

    // Load in dependency order so transitive deps resolve from our dir
    _loaded["avutil"] = LoadWithDiagnosis(Path.Combine(dir, MapName("avutil")));
    _loaded["swscale"] = LoadWithDiagnosis(Path.Combine(dir, MapName("swscale")));
    _loaded["avcodec"] = LoadWithDiagnosis(Path.Combine(dir, MapName("avcodec")));

    NativeLibrary.SetDllImportResolver(typeof(FFAvCodec).Assembly,
      (name, _, _) => _loaded.TryGetValue(name, out var h) ? h : IntPtr.Zero);
  }

  private static IntPtr LoadWithDiagnosis(string path)
  {
    try
    {
      return NativeLibrary.Load(path);
    }
    catch (DllNotFoundException) when (OperatingSystem.IsWindows())
    {
      var missing = new List<string>();
      try
      {
        foreach (var dll in ReadPeImportedDllNames(path))
        {
          var h = LoadLibraryExW(dll, IntPtr.Zero, 0);
          if (h == IntPtr.Zero)
            missing.Add($"{dll} (win32 {Marshal.GetLastPInvokeError()})");
          else
            FreeLibrary(h);
        }
      }
      catch
      {
        // PE parse failed; fall through with whatever we collected
      }

      if (missing.Count == 0)
        throw;

      throw new DllNotFoundException(
        $"Unable to load {path} - missing transitive dependencies: {string.Join(", ", missing)}");
    }
  }

  private static IEnumerable<string> ReadPeImportedDllNames(string path)
  {
    using var stream = File.OpenRead(path);
    using var reader = new PEReader(stream);

    var peHeader = reader.PEHeaders.PEHeader;
    if (peHeader is null) yield break;
    var importDir = peHeader.ImportTableDirectory;
    if (importDir.Size == 0) yield break;

    var block = reader.GetSectionData(importDir.RelativeVirtualAddress);
    var blockReader = block.GetReader();

    while (blockReader.RemainingBytes >= 20)
    {
      blockReader.ReadUInt32(); // OriginalFirstThunk
      blockReader.ReadUInt32(); // TimeDateStamp
      blockReader.ReadUInt32(); // ForwarderChain
      var nameRva = blockReader.ReadInt32();
      blockReader.ReadUInt32(); // FirstThunk

      if (nameRva == 0) yield break;

      var nameBlock = reader.GetSectionData(nameRva);
      var nameReader = nameBlock.GetReader();
      var bytes = new List<byte>();
      while (nameReader.RemainingBytes > 0)
      {
        var b = nameReader.ReadByte();
        if (b == 0) break;
        bytes.Add(b);
      }
      yield return Encoding.ASCII.GetString(bytes.ToArray());
    }
  }

  [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
  private static extern IntPtr LoadLibraryExW(string lpLibFileName, IntPtr hFile, uint dwFlags);

  [DllImport("kernel32")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool FreeLibrary(IntPtr hModule);

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

    if (File.Exists(Path.Combine(baseDir, MapName("avutil"))))
      return baseDir;

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
