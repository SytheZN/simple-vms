using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Models;

namespace Data.Sqlite;

public sealed class SqlitePlugin : IPlugin
{
  private SqliteDataProvider? _provider;

  public PluginMetadata Metadata { get; } = new()
  {
    Id = "sqlite",
    Name = "SQLite Data Provider",
    Version = "1.0.0",
    Description = "Data provider backed by SQLite"
  };

  public OneOf<Success, Error> ConfigureServices(IServiceCollection services)
  {
    services.AddSingleton<IDataProvider>(sp =>
    {
      var config = sp.GetRequiredService<IConfiguration>();
      var dataPath = config["data-path"] ?? "./data";
      var dbPath = Path.Combine(dataPath, "server.db");
      _provider = new SqliteDataProvider(dbPath);
      return _provider;
    });
    return new Success();
  }

  public async Task<OneOf<Success, Error>> StartAsync(CancellationToken ct)
  {
    if (_provider != null)
      return await _provider.MigrateAsync(ct);
    return new Success();
  }

  public Task<OneOf<Success, Error>> StopAsync(CancellationToken ct)
  {
    return Task.FromResult<OneOf<Success, Error>>(new Success());
  }
}
