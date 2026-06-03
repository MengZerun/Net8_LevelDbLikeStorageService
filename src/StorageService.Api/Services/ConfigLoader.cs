using System.Text.Json;
using StorageService.Api.Models;

namespace StorageService.Api.Services;

public interface IConfigLoader
{
    RuntimeConfig Load(string[] args, string contentRootPath);
}

public sealed class ConfigLoader : IConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public RuntimeConfig Load(string[] args, string contentRootPath)
    {
        var configPath = ResolveConfigPath(args, contentRootPath);
        var baseDirectory = System.IO.Path.GetDirectoryName(configPath) ?? contentRootPath;
        var main = ReadJson(configPath, new MainConfig());

        var dbConfig = ReadDbEngineConfig(ResolvePath(baseDirectory, main.DbConfig));
        var dbListPath = ResolvePath(baseDirectory, main.DbList);
        var databases = File.Exists(dbListPath)
            ? JsonSerializer.Deserialize<Dictionary<string, DbDescriptor>>(File.ReadAllText(dbListPath), JsonOptions) ?? new()
            : new Dictionary<string, DbDescriptor>(StringComparer.Ordinal);

        var logConfig = ReadLogConfig(ResolvePath(baseDirectory, main.LogConfig));
        var serverEndpoints = ReadJson(ResolvePath(baseDirectory, main.ServerConfig), Array.Empty<ServerEndpointConfig>());

        NormalizeConfigNames(dbConfig, logConfig);

        return new RuntimeConfig
        {
            ConfigPath = configPath,
            BaseDirectory = baseDirectory,
            Main = main,
            Db = dbConfig,
            Databases = new Dictionary<string, DbDescriptor>(databases, StringComparer.Ordinal),
            Log = logConfig,
            ServerEndpoints = serverEndpoints
        };
    }

    public static string ResolveConfigPath(string[] args, string contentRootPath)
    {
        var envPath = Environment.GetEnvironmentVariable("STORAGE_CONFIG_PATH");
        if (!string.IsNullOrWhiteSpace(envPath))
        {
            return FullPathWithFallback(envPath, contentRootPath);
        }

        for (var i = 0; i < args.Length; i++)
        {
            if ((args[i].Equals("--config", StringComparison.OrdinalIgnoreCase) ||
                 args[i].Equals("-config", StringComparison.OrdinalIgnoreCase)) &&
                i + 1 < args.Length)
            {
                return FullPathWithFallback(args[i + 1], contentRootPath);
            }
        }

        return FullPathWithFallback("config/config.json", contentRootPath);
    }

    private static string FullPathWithFallback(string path, string contentRootPath)
    {
        var candidate = System.IO.Path.IsPathRooted(path)
            ? path
            : System.IO.Path.GetFullPath(System.IO.Path.Combine(Environment.CurrentDirectory, path));

        if (File.Exists(candidate))
        {
            return candidate;
        }

        if (!path.Contains(System.IO.Path.DirectorySeparatorChar) &&
            !path.Contains(System.IO.Path.AltDirectorySeparatorChar))
        {
            var configCandidate = System.IO.Path.GetFullPath(System.IO.Path.Combine(Environment.CurrentDirectory, "config", path));
            if (File.Exists(configCandidate))
            {
                return configCandidate;
            }
        }

        var contentCandidate = System.IO.Path.IsPathRooted(path)
            ? path
            : System.IO.Path.GetFullPath(System.IO.Path.Combine(contentRootPath, path));
        if (File.Exists(contentCandidate))
        {
            return contentCandidate;
        }

        if (!path.Contains(System.IO.Path.DirectorySeparatorChar) &&
            !path.Contains(System.IO.Path.AltDirectorySeparatorChar))
        {
            var contentConfigCandidate = System.IO.Path.GetFullPath(System.IO.Path.Combine(contentRootPath, "config", path));
            if (File.Exists(contentConfigCandidate))
            {
                return contentConfigCandidate;
            }
        }

        return contentCandidate;
    }

    private static string ResolvePath(string baseDirectory, string path)
    {
        if (System.IO.Path.IsPathRooted(path))
        {
            return path;
        }

        var direct = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDirectory, path));
        if (File.Exists(direct))
        {
            return direct;
        }

        var fileNameOnly = System.IO.Path.GetFileName(path);
        return System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDirectory, fileNameOnly));
    }

    private static T ReadJson<T>(string path, T fallback)
    {
        if (!File.Exists(path))
        {
            return fallback;
        }

        var json = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(json) || json.Trim() == "{}")
        {
            return fallback;
        }

        return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? fallback;
    }

    private static DbEngineConfig ReadDbEngineConfig(string path)
    {
        var config = ReadJson(path, new DbEngineConfig());
        if (!File.Exists(path))
        {
            return config;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return config;
        }

        var root = document.RootElement;
        config.DbBackPath = GetString(root, "db_back_path", config.DbBackPath);
        config.RootPath = GetString(root, "root_path", config.RootPath);
        config.HttpPort = GetInt(root, "http_port", config.HttpPort);
        config.DiskCheckSpace = GetDouble(root, "diskCheckSpace", config.DiskCheckSpace);
        config.IsEncrypt = GetBool(root, "is_encrypt", config.IsEncrypt);
        config.EncryptionStr = GetString(root, "encryption_str", config.EncryptionStr);
        config.Engine = GetString(root, "engine", config.Engine);
        config.EnableAdminApi = GetBool(root, "enableAdminApi", config.EnableAdminApi);
        config.MaxRequestBodyMb = GetLong(root, "maxRequestBodyMb", config.MaxRequestBodyMb);
        config.ZmqEnabled = GetBool(root, "zmq_enabled", config.ZmqEnabled);
        config.ZmqStrictStartup = GetBool(root, "zmq_strict_startup", config.ZmqStrictStartup);
        return config;
    }

    private static LogConfig ReadLogConfig(string path)
    {
        var config = ReadJson(path, new LogConfig());
        if (!File.Exists(path))
        {
            return config;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return config;
        }

        var root = document.RootElement;
        config.LogPath = GetString(root, "log_path", config.LogPath);
        config.LogCleanDays = GetInt(root, "log_clean_days", config.LogCleanDays);
        config.MinLogLevel = GetInt(root, "min_log_level", config.MinLogLevel);
        return config;
    }

    private static string GetString(JsonElement root, string name, string fallback) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    private static int GetInt(JsonElement root, string name, int fallback) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : fallback;

    private static long GetLong(JsonElement root, string name, long fallback) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number
            : fallback;

    private static double GetDouble(JsonElement root, string name, double fallback) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)
            ? number
            : fallback;

    private static bool GetBool(JsonElement root, string name, bool fallback) =>
        root.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => bool.TryParse(value.GetString(), out var parsed) ? parsed : fallback,
                _ => fallback
            }
            : fallback;

    private static void NormalizeConfigNames(DbEngineConfig db, LogConfig log)
    {
        if (string.IsNullOrWhiteSpace(db.Engine))
        {
            db.Engine = "file";
        }

        if (db.HttpPort <= 0)
        {
            db.HttpPort = 9877;
        }

        if (db.MaxRequestBodyMb <= 0)
        {
            db.MaxRequestBodyMb = 100;
        }

        if (string.IsNullOrWhiteSpace(log.LogPath))
        {
            log.LogPath = "./log/";
        }
    }
}
