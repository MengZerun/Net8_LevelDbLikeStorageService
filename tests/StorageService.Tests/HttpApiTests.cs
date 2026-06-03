using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace StorageService.Tests;

[CollectionDefinition("HttpApi", DisableParallelization = true)]
public sealed class HttpApiCollection;

[Collection("HttpApi")]
public sealed class HttpApiTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public HttpApiTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "Net8_LevelDbLikeStorageServiceTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        var configPath = CreateConfig(_tempRoot);
        Environment.SetEnvironmentVariable("STORAGE_CONFIG_PATH", configPath);
        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task CrudAndPrefixQueriesWorkOverHttp()
    {
        var put = await PostAsync(new
        {
            db_name = "tray",
            is_batch = "false",
            key = "T001-001",
            key_list = "",
            op_mode = "all_ow",
            operation = "put",
            uniqueKey = "put001",
            value = "{\"a\":1}"
        });
        Assert.Equal(0, put.GetProperty("resultCode").GetInt32());

        var get = await PostAsync(new
        {
            db_name = "tray",
            key = "T001-001",
            key_list = "",
            op_mode = "all",
            operation = "get",
            uniqueKey = "get001",
            value = ""
        });
        Assert.Equal("{\"a\":1}", get.GetProperty("value").GetString());

        await PostAsync(new { db_name = "tray", key = "T001-002", key_list = "", op_mode = "all_ow", operation = "put", uniqueKey = "put002", value = "{\"a\":2}" });

        var keys = await PostAsync(new { db_name = "tray", key = "T001", key_list = "", op_mode = "prefix_keys", operation = "list", uniqueKey = "keys001", value = "" });
        Assert.Equal("[\"T001-001\",\"T001-002\"]", keys.GetProperty("value").GetString());

        var kvs = await PostAsync(new { db_name = "tray", key = "T001", key_list = "", op_mode = "prefix_kvs", operation = "list", uniqueKey = "kvs001", value = "" });
        Assert.Contains("\"key\":\"T001-001\"", kvs.GetProperty("value").GetString());

        var delete = await PostAsync(new { db_name = "tray", key = "T001-001", key_list = "", op_mode = "all", operation = "delete", uniqueKey = "del001", value = "" });
        Assert.Equal(0, delete.GetProperty("resultCode").GetInt32());
    }

    [Fact]
    public async Task ApAndBatchKvModesWork()
    {
        await PostAsync(new { db_name = "tray_list", key = "trayCodeList", key_list = "", op_mode = "ap", operation = "put", uniqueKey = "ap001", value = "T001" });
        await PostAsync(new { db_name = "tray_list", key = "trayCodeList", key_list = "", op_mode = "ap", operation = "put", uniqueKey = "ap002", value = "T002" });

        var apGet = await PostAsync(new { db_name = "tray_list", key = "trayCodeList", key_list = "", op_mode = "all", operation = "get", uniqueKey = "apget", value = "" });
        Assert.Equal("[\"T001\",\"T002\"]", apGet.GetProperty("value").GetString());

        var batchValue = new[]
        {
            new { key = "B001", value = "{\"b\":1}" },
            new { key = "B002", value = "{\"b\":2}" }
        };

        var batch = await PostAsync(new { db_name = "tray", is_batch = true, key = "", key_list = "", op_mode = "kv", operation = "put", uniqueKey = "batch001", value = batchValue });
        Assert.Equal(0, batch.GetProperty("resultCode").GetInt32());

        var last = await PostAsync(new { db_name = "tray", key = "B", key_list = "", op_mode = "last", operation = "get", uniqueKey = "last001", value = "" });
        Assert.Equal("{\"b\":2}", last.GetProperty("value").GetString());
    }

    [Fact]
    public async Task MissingAndInactiveDbReturnCompatibilityErrors()
    {
        var missing = await PostAsync(new { db_name = "missing", key = "k", key_list = "", op_mode = "all", operation = "get", uniqueKey = "missing001", value = "" });
        Assert.Equal(-201, missing.GetProperty("resultCode").GetInt32());

        var inactive = await PostAsync(new { db_name = "inactive_db", key = "k", key_list = "", op_mode = "all", operation = "get", uniqueKey = "inactive001", value = "" });
        Assert.Equal(-201, inactive.GetProperty("resultCode").GetInt32());
    }

    [Fact]
    public async Task UnsupportedOperationAndModeReturnErrors()
    {
        var badOp = await PostAsync(new { db_name = "tray", key = "k", key_list = "", op_mode = "all", operation = "update", uniqueKey = "badop", value = "" });
        Assert.Equal(-301, badOp.GetProperty("resultCode").GetInt32());

        var badMode = await PostAsync(new { db_name = "tray", key = "k", key_list = "", op_mode = "xxx", operation = "get", uniqueKey = "badmode", value = "" });
        Assert.Equal(-302, badMode.GetProperty("resultCode").GetInt32());
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        Environment.SetEnvironmentVariable("STORAGE_CONFIG_PATH", null);
        if (Directory.Exists(_tempRoot))
        {
            try
            {
                Directory.Delete(_tempRoot, true);
            }
            catch (IOException)
            {
                // log4net can hold file appender handles until process shutdown.
            }
        }
    }

    private async Task<JsonElement> PostAsync(object request)
    {
        var json = JsonSerializer.Serialize(request);
        using var response = await _client.PostAsync("/", new StringContent(json, Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static string CreateConfig(string root)
    {
        var configDir = Path.Combine(root, "config");
        Directory.CreateDirectory(configDir);
        File.WriteAllText(Path.Combine(configDir, "config.json"), """
        {
          "dbConfig": "config/db_config.json",
          "dbList": "config/db.json",
          "serverConfig": "config/server_config.json",
          "logConfig": "config/log_config.json"
        }
        """);
        File.WriteAllText(Path.Combine(configDir, "db_config.json"), $$"""
        {
          "root_path": "{{root.Replace("\\", "\\\\")}}",
          "http_port": 9877,
          "diskCheckSpace": 0,
          "engine": "file",
          "maxRequestBodyMb": 10
        }
        """);
        File.WriteAllText(Path.Combine(configDir, "db.json"), $$"""
        {
          "tray": { "path": "{{Path.Combine(root, "tray").Replace("\\", "\\\\")}}", "status": "active", "version": "0.0.0.1" },
          "tray_list": { "path": "{{Path.Combine(root, "tray_list").Replace("\\", "\\\\")}}", "status": "active", "version": "0.0.0.1" },
          "inactive_db": { "path": "{{Path.Combine(root, "inactive_db").Replace("\\", "\\\\")}}", "status": "inactive", "version": "0.0.0.1" }
        }
        """);
        File.WriteAllText(Path.Combine(configDir, "log_config.json"), $$"""
        {
          "log_path": "{{Path.Combine(root, "log").Replace("\\", "\\\\")}}",
          "log_clean_days": 1,
          "min_log_level": 2
        }
        """);
        File.WriteAllText(Path.Combine(configDir, "server_config.json"), "[]");
        File.WriteAllText(Path.Combine(configDir, "get_all_file.json"), "{}");
        return Path.Combine(configDir, "config.json");
    }
}
