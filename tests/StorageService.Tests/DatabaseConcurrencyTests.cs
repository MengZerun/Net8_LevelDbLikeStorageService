using System.Diagnostics;
using Xunit.Abstractions;
using StorageService.Api.Storage;

namespace StorageService.Tests;

public sealed class DatabaseConcurrencyTests : IDisposable
{
    private const int WorkerCount = 32;
    private const int ItemsPerWorker = 250;
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "Net8LevelDbConcurrencyTests", Guid.NewGuid().ToString("N"));
    private readonly ITestOutputHelper _output;

    public DatabaseConcurrencyTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task LevelDbEngineHandlesHighConcurrencyAndPrintsMetrics()
    {
        var totalItems = WorkerCount * ItemsPerWorker;
        var engine = new LevelDbKvEngine();
        var db = await engine.OpenAsync("concurrency_leveldb", _tempRoot, CancellationToken.None);

        try
        {
            var writeWatch = Stopwatch.StartNew();
            await Task.WhenAll(Enumerable.Range(0, WorkerCount).Select(worker => Task.Run(async () =>
            {
                for (var i = 0; i < ItemsPerWorker; i++)
                {
                    var key = BuildKey(worker, i);
                    var value = $$"""{"worker":{{worker}},"index":{{i}},"result":"OK"}""";
                    await db.PutAsync(key, value, CancellationToken.None);
                }
            })));
            writeWatch.Stop();

            var readWatch = Stopwatch.StartNew();
            var foundCount = 0;
            await Task.WhenAll(Enumerable.Range(0, WorkerCount).Select(worker => Task.Run(async () =>
            {
                for (var i = 0; i < ItemsPerWorker; i++)
                {
                    var value = await db.GetAsync(BuildKey(worker, i), CancellationToken.None);
                    if (!string.IsNullOrEmpty(value))
                    {
                        Interlocked.Increment(ref foundCount);
                    }
                }
            })));
            readWatch.Stop();

            var listWatch = Stopwatch.StartNew();
            var prefixKeys = await db.ListKeysByPrefixAsync("concurrency:", null, CancellationToken.None);
            listWatch.Stop();

            var report = string.Join(Environment.NewLine,
                "LevelDB high concurrency test result:",
                $"  workers              : {WorkerCount}",
                $"  items per worker     : {ItemsPerWorker}",
                $"  total writes         : {totalItems}",
                $"  total reads          : {totalItems}",
                $"  successful reads     : {foundCount}",
                $"  prefix key count     : {prefixKeys.Count}",
                $"  write elapsed        : {writeWatch.ElapsedMilliseconds} ms",
                $"  read elapsed         : {readWatch.ElapsedMilliseconds} ms",
                $"  prefix list elapsed  : {listWatch.ElapsedMilliseconds} ms",
                $"  write throughput     : {CalculateOps(totalItems, writeWatch.Elapsed):F2} ops/s",
                $"  read throughput      : {CalculateOps(totalItems, readWatch.Elapsed):F2} ops/s");

            _output.WriteLine(report);
            Console.WriteLine(report);

            Assert.Equal(totalItems, foundCount);
            Assert.Equal(totalItems, prefixKeys.Count);
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

    private static string BuildKey(int worker, int index) => $"concurrency:{worker:D2}:{index:D5}";

    private static double CalculateOps(int count, TimeSpan elapsed) =>
        count / Math.Max(elapsed.TotalSeconds, 0.001);
}
