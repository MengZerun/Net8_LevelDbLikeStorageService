namespace StorageService.Api.Storage;

public interface IKvDatabase
{
    string Name { get; }
    string Path { get; }
    Task PutAsync(string key, string value, CancellationToken ct);
    Task PutManyAsync(IReadOnlyList<KeyValuePair<string, string>> values, CancellationToken ct);
    Task<string?> GetAsync(string key, CancellationToken ct);
    Task<bool> DeleteAsync(string key, CancellationToken ct);
    Task<IReadOnlyList<string>> ListKeysByPrefixAsync(string prefix, int? limit, CancellationToken ct);
    Task<IReadOnlyList<KeyValuePair<string, string>>> ListKvsByPrefixAsync(string prefix, int? limit, CancellationToken ct);
    Task CloseAsync();
}

public interface IKvEngine
{
    Task<IKvDatabase> OpenAsync(string name, string path, CancellationToken ct);
}
