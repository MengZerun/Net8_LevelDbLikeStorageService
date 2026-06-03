using log4net;
using StorageService.Api.Models;
using StorageService.Api.Storage;

namespace StorageService.Api.Services;

public interface IDatabaseManager
{
    Task InitializeAsync(CancellationToken ct);
    bool TryGetDatabase(string dbName, out IKvDatabase? database, out DbResponse? error, string uniqueKey);
    IReadOnlyDictionary<string, DbDescriptor> Descriptors { get; }
    IReadOnlyDictionary<string, IKvDatabase> OpenedDatabases { get; }
    Task CloseAllAsync();
}

public sealed class DatabaseManager : IDatabaseManager
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(DatabaseManager));
    private readonly RuntimeConfig _config;
    private readonly IKvEngine _engine;
    private readonly Dictionary<string, IKvDatabase> _opened = new(StringComparer.Ordinal);

    public DatabaseManager(RuntimeConfig config, IKvEngine engine)
    {
        _config = config;
        _engine = engine;
    }

    public IReadOnlyDictionary<string, DbDescriptor> Descriptors => _config.Databases;
    public IReadOnlyDictionary<string, IKvDatabase> OpenedDatabases => _opened;

    public async Task InitializeAsync(CancellationToken ct)
    {
        foreach (var (name, descriptor) in _config.Databases)
        {
            if (!descriptor.Status.Equals("active", StringComparison.OrdinalIgnoreCase))
            {
                Log.Info($"Skip inactive db name={name} path={descriptor.Path}");
                continue;
            }

            var dbPath = ResolveDbPath(name, descriptor.Path);
            Directory.CreateDirectory(dbPath);
            _opened[name] = await _engine.OpenAsync(name, dbPath, ct);
            Log.Info($"Opened db name={name} path={dbPath}");
        }

        if (_config.ServerEndpoints.Count > 0)
        {
            Log.Info($"Loaded {_config.ServerEndpoints.Count} server_config endpoints. ZeroMQ compatibility is not enabled in phase one.");
        }
    }

    public bool TryGetDatabase(string dbName, out IKvDatabase? database, out DbResponse? error, string uniqueKey)
    {
        database = null;
        error = null;

        if (!_config.Databases.TryGetValue(dbName, out var descriptor))
        {
            error = DbResponse.Error(-201, $"db_name not found: {dbName}", uniqueKey);
            return false;
        }

        if (!descriptor.Status.Equals("active", StringComparison.OrdinalIgnoreCase))
        {
            error = DbResponse.Error(-201, $"db_name inactive: {dbName}", uniqueKey);
            return false;
        }

        if (!_opened.TryGetValue(dbName, out database))
        {
            error = DbResponse.Error(-500, $"database is not opened: {dbName}", uniqueKey);
            return false;
        }

        return true;
    }

    public async Task CloseAllAsync()
    {
        foreach (var database in _opened.Values)
        {
            await database.CloseAsync();
        }
    }

    private string ResolveDbPath(string dbName, string configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return configuredPath;
        }

        return System.IO.Path.Combine(_config.Db.RootPath, dbName);
    }
}
