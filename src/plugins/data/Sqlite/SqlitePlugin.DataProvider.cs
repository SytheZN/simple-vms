using DbUp;
using DbUp.Engine;
using DbUp.Engine.Output;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Shared.Models;

namespace Data.Sqlite;

public sealed partial class SqliteProvider : IDataProvider
{
  private const ushort ModuleId = ModuleIds.PluginSqliteMigration;
  private readonly ConnectionQueue _queue = new();
  private SqliteConnection? _connection;

  public string ProviderId => "sqlite";

  public ICameraRepository Cameras { get; private set; } = null!;
  public IStreamRepository Streams { get; private set; } = null!;
  public ISegmentRepository Segments { get; private set; } = null!;
  public IKeyframeRepository Keyframes { get; private set; } = null!;
  public IEventRepository Events { get; private set; } = null!;
  public IClientRepository Clients { get; private set; } = null!;
  public IConfigRepository Config { get; private set; } = null!;

  internal void InitializeProvider(string databasePath)
  {
    var dir = Path.GetDirectoryName(databasePath);
    if (dir != null)
      Directory.CreateDirectory(dir);

    var connectionString = new SqliteConnectionStringBuilder
    {
      DataSource = databasePath,
      Mode = SqliteOpenMode.ReadWriteCreate,
      Cache = SqliteCacheMode.Shared
    }.ToString();

    _connection = new SqliteConnection(connectionString);
    _connection.Open();

    using (var pragma = _connection.CreateCommand())
    {
      pragma.CommandText = "PRAGMA locking_mode = EXCLUSIVE";
      pragma.ExecuteNonQuery();
    }
    using (var pragma = _connection.CreateCommand())
    {
      pragma.CommandText = "PRAGMA journal_mode = WAL";
      pragma.ExecuteNonQuery();
    }
    using (var pragma = _connection.CreateCommand())
    {
      pragma.CommandText = "PRAGMA synchronous = NORMAL";
      pragma.ExecuteNonQuery();
    }
    using (var pragma = _connection.CreateCommand())
    {
      pragma.CommandText = "PRAGMA foreign_keys = ON";
      pragma.ExecuteNonQuery();
    }

    _queue.Start(work => work(_connection));

    Cameras = new CameraRepository(_queue);
    Streams = new StreamRepository(_queue);
    Segments = new SegmentRepository(_queue);
    Keyframes = new KeyframeRepository(_queue);
    Events = new EventRepository(_queue);
    Clients = new ClientRepository(_queue);
    Config = new ConfigRepository(_queue);
  }

  public IDataStore GetDataStore(string pluginId)
  {
    return new DataStore(_queue, pluginId);
  }

  internal OneOf<Success, Error> MigrateDatabase(string databasePath, ILogger logger)
  {
    var dir = Path.GetDirectoryName(databasePath);
    if (dir != null)
      Directory.CreateDirectory(dir);

    var connectionString = new SqliteConnectionStringBuilder
    {
      DataSource = databasePath,
      Mode = SqliteOpenMode.ReadWriteCreate,
      Pooling = false
    }.ToString();

    var upgrader = DeployChanges.To
      .SqliteDatabase(connectionString)
      .WithScripts(MigrationScripts.All)
      .LogTo(new MigrationLog(logger))
      .Build();

    var result = upgrader.PerformUpgrade();
    if (!result.Successful)
      return Error.Create(ModuleId, 0x0001, Result.InternalError,
        $"Migration failed at '{result.ErrorScript?.Name}': {result.Error.Message}");

    return new Success();
  }

  private sealed class MigrationLog(ILogger logger) : IUpgradeLog
  {
    public void LogTrace(string format, params object[] args) =>
      logger.LogTrace(format, args);
    public void LogDebug(string format, params object[] args) =>
      logger.LogDebug(format, args);
    public void LogInformation(string format, params object[] args) =>
      logger.LogInformation(format, args);
    public void LogWarning(string format, params object[] args) =>
      logger.LogWarning(format, args);
    public void LogError(string format, params object[] args) =>
      logger.LogError(format, args);
    public void LogError(Exception ex, string format, params object[] args) =>
      logger.LogError(ex, format, args);
  }
}
