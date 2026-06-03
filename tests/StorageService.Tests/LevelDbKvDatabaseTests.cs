using StorageService.Api.Storage;

namespace StorageService.Tests;

public sealed class LevelDbKvDatabaseTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "Net8LevelDbNativeTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task LevelDbEnginePersistsCrudBatchAndPrefixQueries()
    {
        var engine = new LevelDbKvEngine();
        var db = await engine.OpenAsync("native_leveldb", _tempRoot, CancellationToken.None);
        try
        {
            await db.PutAsync("A001", "{\"a\":1}", CancellationToken.None);
            Assert.Equal("{\"a\":1}", await db.GetAsync("A001", CancellationToken.None));

            await db.PutManyAsync(
            [
                new KeyValuePair<string, string>("A002", "{\"a\":2}"),
                new KeyValuePair<string, string>("B001", "{\"b\":1}")
            ], CancellationToken.None);

            var keys = await db.ListKeysByPrefixAsync("A", null, CancellationToken.None);
            Assert.Equal(["A001", "A002"], keys);

            var kvs = await db.ListKvsByPrefixAsync("A", null, CancellationToken.None);
            Assert.Equal("A002", kvs[1].Key);
            Assert.Equal("{\"a\":2}", kvs[1].Value);

            Assert.True(await db.DeleteAsync("A001", CancellationToken.None));
            Assert.Null(await db.GetAsync("A001", CancellationToken.None));
        }
        finally
        {
            await db.CloseAsync();
        }

        db = await engine.OpenAsync("native_leveldb", _tempRoot, CancellationToken.None);
        try
        {
            Assert.Equal("{\"a\":2}", await db.GetAsync("A002", CancellationToken.None));
        }
        finally
        {
            await db.CloseAsync();
        }
    }

    public void Dispose()
    {
        if (!Directory.Exists(_tempRoot))
        {
            return;
        }

        try
        {
            Directory.Delete(_tempRoot, true);
        }
        catch (IOException)
        {
            // LevelDB may release native file handles shortly after Close on Windows.
        }
    }
}
