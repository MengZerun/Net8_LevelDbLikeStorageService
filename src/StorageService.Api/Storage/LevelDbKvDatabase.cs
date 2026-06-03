using LevelDB;

namespace StorageService.Api.Storage;

public sealed class LevelDbKvEngine : IKvEngine
{
    public Task<IKvDatabase> OpenAsync(string name, string path, CancellationToken ct)
    {
        Directory.CreateDirectory(path);
        return Task.FromResult<IKvDatabase>(new LevelDbKvDatabase(name, path));
    }
}

public sealed class LevelDbKvDatabase : IKvDatabase
{
    private readonly DB _db;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private bool _closed;

    public LevelDbKvDatabase(string name, string path)
    {
        Name = name;
        Path = path;

        var options = new Options
        {
            CreateIfMissing = true
        };
        _db = new DB(options, path);
    }

    public string Name { get; }
    public string Path { get; }

    public async Task PutAsync(string key, string value, CancellationToken ct)
    {
        await _writeGate.WaitAsync(ct);
        try
        {
            ThrowIfClosed();
            _db.Put(key, value);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task PutManyAsync(IReadOnlyList<KeyValuePair<string, string>> values, CancellationToken ct)
    {
        await _writeGate.WaitAsync(ct);
        try
        {
            ThrowIfClosed();
            using var batch = new WriteBatch();
            foreach (var pair in values)
            {
                batch.Put(pair.Key, pair.Value);
            }

            _db.Write(batch);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public Task<string?> GetAsync(string key, CancellationToken ct)
    {
        ThrowIfClosed();
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<string?>(_db.Get(key));
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken ct)
    {
        await _writeGate.WaitAsync(ct);
        try
        {
            ThrowIfClosed();
            _db.Delete(key);
            return true;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public Task<IReadOnlyList<string>> ListKeysByPrefixAsync(string prefix, int? limit, CancellationToken ct)
    {
        ThrowIfClosed();
        var result = new List<string>();
        using var iterator = _db.CreateIterator();

        for (iterator.Seek(prefix); iterator.IsValid(); iterator.Next())
        {
            ct.ThrowIfCancellationRequested();
            var key = iterator.KeyAsString();
            if (!key.StartsWith(prefix, StringComparison.Ordinal))
            {
                break;
            }

            result.Add(key);
            if (limit is > 0 && result.Count >= limit.Value)
            {
                break;
            }
        }

        return Task.FromResult<IReadOnlyList<string>>(result);
    }

    public Task<IReadOnlyList<KeyValuePair<string, string>>> ListKvsByPrefixAsync(string prefix, int? limit, CancellationToken ct)
    {
        ThrowIfClosed();
        var result = new List<KeyValuePair<string, string>>();
        using var iterator = _db.CreateIterator();

        for (iterator.Seek(prefix); iterator.IsValid(); iterator.Next())
        {
            ct.ThrowIfCancellationRequested();
            var key = iterator.KeyAsString();
            if (!key.StartsWith(prefix, StringComparison.Ordinal))
            {
                break;
            }

            result.Add(new KeyValuePair<string, string>(key, iterator.ValueAsString()));
            if (limit is > 0 && result.Count >= limit.Value)
            {
                break;
            }
        }

        return Task.FromResult<IReadOnlyList<KeyValuePair<string, string>>>(result);
    }

    public async Task CloseAsync()
    {
        await _writeGate.WaitAsync();
        try
        {
            if (_closed)
            {
                return;
            }

            _db.Close();
            _closed = true;
        }
        finally
        {
            _writeGate.Release();
            _writeGate.Dispose();
        }
    }

    private void ThrowIfClosed()
    {
        if (_closed)
        {
            throw new ObjectDisposedException(Name);
        }
    }
}

public sealed class RocksDbKvDatabase
{
    public const string CompatibilityNote = "RocksDB engine placeholder. Configure engine=file or engine=leveldb in phase one.";
}
