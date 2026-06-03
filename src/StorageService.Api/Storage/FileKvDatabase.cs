using System.Text.Encodings.Web;
using System.Text.Json;

namespace StorageService.Api.Storage;

public sealed class FileKvEngine : IKvEngine
{
    public Task<IKvDatabase> OpenAsync(string name, string path, CancellationToken ct) =>
        FileKvDatabase.OpenAsync(name, path, ct);
}

public sealed class FileKvDatabase : IKvDatabase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SortedDictionary<string, string> _data = new(StringComparer.Ordinal);
    private readonly string _filePath;
    private bool _closed;

    private FileKvDatabase(string name, string path)
    {
        Name = name;
        Path = path;
        _filePath = System.IO.Path.Combine(path, "data.json");
    }

    public string Name { get; }
    public string Path { get; }

    public static async Task<IKvDatabase> OpenAsync(string name, string path, CancellationToken ct)
    {
        Directory.CreateDirectory(path);
        var db = new FileKvDatabase(name, path);
        await db.LoadAsync(ct);
        return db;
    }

    public async Task PutAsync(string key, string value, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            ThrowIfClosed();
            _data[key] = value;
            await SaveUnsafeAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task PutManyAsync(IReadOnlyList<KeyValuePair<string, string>> values, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            ThrowIfClosed();
            foreach (var pair in values)
            {
                _data[pair.Key] = pair.Value;
            }

            await SaveUnsafeAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string?> GetAsync(string key, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            ThrowIfClosed();
            return _data.TryGetValue(key, out var value) ? value : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            ThrowIfClosed();
            var removed = _data.Remove(key);
            if (removed)
            {
                await SaveUnsafeAsync(ct);
            }

            return removed;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<string>> ListKeysByPrefixAsync(string prefix, int? limit, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            ThrowIfClosed();
            return ApplyLimit(_data.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)), limit).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<KeyValuePair<string, string>>> ListKvsByPrefixAsync(string prefix, int? limit, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            ThrowIfClosed();
            return ApplyLimit(_data.Where(pair => pair.Key.StartsWith(prefix, StringComparison.Ordinal)), limit).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CloseAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (!_closed)
            {
                await SaveUnsafeAsync(CancellationToken.None);
                _closed = true;
            }
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(_filePath))
        {
            await SaveUnsafeAsync(ct);
            return;
        }

        await using var stream = File.OpenRead(_filePath);
        var data = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(stream, JsonOptions, ct);
        if (data is null)
        {
            return;
        }

        foreach (var pair in data)
        {
            _data[pair.Key] = pair.Value;
        }
    }

    private async Task SaveUnsafeAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(Path);
        var tempPath = _filePath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, _data, JsonOptions, ct);
        }

        File.Move(tempPath, _filePath, true);
    }

    private void ThrowIfClosed()
    {
        if (_closed)
        {
            throw new ObjectDisposedException(Name);
        }
    }

    private static IEnumerable<T> ApplyLimit<T>(IEnumerable<T> source, int? limit) =>
        limit is > 0 ? source.Take(limit.Value) : source;
}
