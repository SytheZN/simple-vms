using System.Diagnostics;
using Shared.Models.Formats;

namespace MotionBenchH26x;

internal static class GridEncoder
{
  public static string? Encode(IReadOnlyList<MotionGridUnit> grids, double fps, string path)
  {
    var width = grids[0].Width;
    var height = grids[0].Height;

    var info = new ProcessStartInfo("ffmpeg")
    {
      RedirectStandardInput = true,
      RedirectStandardError = true,
      ArgumentList =
      {
        "-hide_banner", "-loglevel", "error", "-y",
        "-f", "rawvideo", "-pix_fmt", "gray", "-video_size", $"{width}x{height}",
        "-framerate", (fps > 0 ? fps : 25).ToString("F2"), "-i", "-",
        "-vf", "pad=ceil(iw/2)*2:ceil(ih/2)*2",
        "-c:v", "libx264", "-pix_fmt", "yuv420p", path
      }
    };

    Process? process;
    try
    {
      process = Process.Start(info);
    }
    catch (Exception ex)
    {
      return ex.Message;
    }
    if (process == null) return "ffmpeg did not start";

    using (process)
    {
      var stderr = process.StandardError.ReadToEndAsync();
      string? failure = null;

      try
      {
        foreach (var grid in grids)
        {
          if (grid.Width != width || grid.Height != height)
          {
            failure = $"grid dimensions changed from {width}x{height} to {grid.Width}x{grid.Height}";
            break;
          }
          process.StandardInput.BaseStream.Write(grid.Data.Span);
        }
      }
      catch (IOException)
      {
      }

      try { process.StandardInput.Close(); }
      catch (IOException) { }

      process.WaitForExit();
      if (failure != null) return failure;
      if (process.ExitCode == 0) return null;

      var detail = stderr.Result.Trim();
      return detail.Length > 0 ? detail : $"ffmpeg exited with code {process.ExitCode}";
    }
  }
}
