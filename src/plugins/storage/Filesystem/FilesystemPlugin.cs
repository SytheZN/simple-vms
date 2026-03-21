using Shared.Models;

namespace Storage.Filesystem;

public sealed partial class FilesystemPlugin : IPlugin
{
  private IConfig _config = null!;
  private IServerEnvironment _environment = null!;
  private string _rootPath = null!;

  public PluginMetadata Metadata { get; } = new()
  {
    Id = "filesystem",
    Name = "Filesystem Storage",
    Version = "1.0.0",
    Description = "Storage provider for local and network filesystems"
  };

  public OneOf<Success, Error> Initialize(PluginContext context)
  {
    _config = context.Config;
    _environment = context.Environment;
    _rootPath = _config.Get("path", Path.Combine(_environment.DataPath, "recordings"));
    return new Success();
  }

  public Task<OneOf<Success, Error>> StartAsync(CancellationToken ct)
  {
    try
    {
      Directory.CreateDirectory(_rootPath);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      return Task.FromResult<OneOf<Success, Error>>(
        new Error(Result.InternalError,
          new DebugTag(ModuleIds.PluginFilesystemStorage, 0x0001),
          $"Cannot create recordings directory: {ex.Message}"));
    }
    return Task.FromResult<OneOf<Success, Error>>(new Success());
  }

  public Task<OneOf<Success, Error>> StopAsync(CancellationToken ct) =>
    Task.FromResult<OneOf<Success, Error>>(new Success());
}
