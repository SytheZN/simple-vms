using Shared.Models;

namespace Data.Sqlite;

public sealed partial class SqliteProvider : IPlugin
{
  private IConfig _config = null!;
  private IServerEnvironment _environment = null!;

  public PluginMetadata Metadata { get; } = new()
  {
    Id = "sqlite",
    Name = "SQLite Data Provider",
    Version = "1.0.0",
    Description = "Data provider backed by SQLite"
  };

  public OneOf<Success, Error> Initialize(PluginContext context)
  {
    _config = context.Config;
    _environment = context.Environment;
    SQLitePCL.Batteries.Init();
    return new Success();
  }

  public async Task<OneOf<Success, Error>> StartAsync(CancellationToken ct)
  {
    var directory = _config.Get("directory", _environment.DataPath);
    var filename = _config.Get("filename", "server.db");
    InitializeProvider(Path.Combine(directory, filename));
    return await MigrateAsync(ct);
  }

  public Task<OneOf<Success, Error>> StopAsync(CancellationToken ct)
  {
    return Task.FromResult<OneOf<Success, Error>>(new Success());
  }
}
