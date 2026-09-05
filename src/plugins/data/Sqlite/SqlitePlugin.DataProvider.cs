using DbUp;
using DbUp.Engine.Output;
using DbUp.Sqlite.Helpers;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Data.Sqlite;

public sealed partial class SqliteProvider : IDataProvider
{
  private const ushort ModuleId = ModuleIds.PluginSqliteMigration;
  private static readonly TimeSpan CheckpointInterval = TimeSpan.FromMinutes(5);

  private readonly ConnectionQueue _queue = new();
  private SqliteConnection? _connection;
  private Timer? _checkpointTimer;

  public string ProviderId => "sqlite";

  public ICameraRepository Cameras { get; private set; } = null!;
  public IStreamRepository Streams { get; private set; } = null!;
  public ISegmentRepository Segments { get; private set; } = null!;
  public IKeyframeRepository Keyframes { get; private set; } = null!;
  public IEventRepository Events { get; private set; } = null!;
  public ISystemEventRepository SystemEvents { get; private set; } = null!;
  public IClientRepository Clients { get; private set; } = null!;
  public IConfigRepository Config { get; private set; } = null!;

  internal void OpenDatabase(string databasePath)
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

    ExecutePragma("PRAGMA locking_mode = EXCLUSIVE");
    ExecutePragma("PRAGMA journal_mode = WAL");
    ExecutePragma("PRAGMA synchronous = NORMAL");
    ExecutePragma("PRAGMA foreign_keys = ON");
  }

  internal void CloseDatabase()
  {
    _checkpointTimer?.Dispose();
    _checkpointTimer = null;

    _connection?.Dispose();
    _connection = null;
  }

  internal OneOf<Success, Error> MigrateDatabase(ILogger logger)
  {
    var upgrader = DeployChanges.To
      .SqliteDatabase(new SharedConnection(_connection!))
      .WithScripts(MigrationScripts.All)
      .LogTo(new MigrationLog(logger))
      .Build();

    var result = upgrader.PerformUpgrade();
    if (!result.Successful)
      return Error.Create(ModuleId, 0x0001, Result.InternalError,
        $"Migration failed at '{result.ErrorScript?.Name}': {result.Error}");

    return new Success();
  }

  internal void InitializeProvider()
  {
    _queue.Start(work => work(_connection!));

    Cameras = new CameraRepository(_queue);
    Streams = new StreamRepository(_queue);
    Segments = new SegmentRepository(_queue);
    Keyframes = new KeyframeRepository(_queue);
    Events = new EventRepository(_queue);
    SystemEvents = new SystemEventRepository(_queue);
    Clients = new ClientRepository(_queue);
    Config = new ConfigRepository(_queue);

    _checkpointTimer = new Timer(
      _ => Checkpoint(), null, CheckpointInterval, CheckpointInterval);
  }

  private void Checkpoint()
  {
    _ = _queue.ExecuteAsync(connection =>
    {
      using var command = connection.CreateCommand();
      command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
      command.ExecuteNonQuery();
      return true;
    }, CancellationToken.None)
      .ContinueWith(
        task => _logger.LogWarning(task.Exception, "WAL checkpoint failed"),
        CancellationToken.None,
        TaskContinuationOptions.OnlyOnFaulted,
        TaskScheduler.Default);
  }

  private void ExecutePragma(string sql)
  {
    using var command = _connection!.CreateCommand();
    command.CommandText = sql;
    command.ExecuteNonQuery();
  }

  public IDataStore GetDataStore(string pluginId)
  {
    return new DataStore(_queue, pluginId);
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
