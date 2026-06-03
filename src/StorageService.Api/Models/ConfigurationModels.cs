namespace StorageService.Api.Models;

public sealed class MainConfig
{
    public string DbConfig { get; set; } = "config/db_config.json";
    public string DbList { get; set; } = "config/db.json";
    public string ServerConfig { get; set; } = "config/server_config.json";
    public string LogConfig { get; set; } = "config/log_config.json";
}

public sealed class DbEngineConfig
{
    public string DbBackPath { get; set; } = "D:/dlib";
    public string RootPath { get; set; } = "D:/dlib";
    public int HttpPort { get; set; } = 9877;
    public double DiskCheckSpace { get; set; } = 1.0;
    public bool IsEncrypt { get; set; }
    public string EncryptionStr { get; set; } = "encrypted";
    public string Engine { get; set; } = "file";
    public bool EnableAdminApi { get; set; }
    public long MaxRequestBodyMb { get; set; } = 100;
    public bool ZmqEnabled { get; set; } = true;
    public bool ZmqStrictStartup { get; set; }
}

public sealed class DbDescriptor
{
    public string Path { get; set; } = "";
    public string Status { get; set; } = "active";
    public string Version { get; set; } = "0.0.0.1";
}

public sealed class LogConfig
{
    public string LogPath { get; set; } = "./log/";
    public int LogCleanDays { get; set; } = 3;
    public int MinLogLevel { get; set; }
}

public sealed class ServerEndpointConfig
{
    public string? Id { get; set; }
    public string? ServerId { get; set; }
    public bool Enabled { get; set; } = true;
    public bool? Bind { get; set; }
    public string? Description { get; set; }
    public string[] Filter { get; set; } = [];
    public string Ip { get; set; } = "*";
    public string Mode { get; set; } = "";
    public string Port { get; set; } = "";
    public string Protocol { get; set; } = "tcp";
    public string[] Topic { get; set; } = [];
    public string Type { get; set; } = "";
}

public sealed class RuntimeConfig
{
    public string ConfigPath { get; init; } = "";
    public string BaseDirectory { get; init; } = "";
    public MainConfig Main { get; init; } = new();
    public DbEngineConfig Db { get; init; } = new();
    public Dictionary<string, DbDescriptor> Databases { get; init; } = new(StringComparer.Ordinal);
    public LogConfig Log { get; init; } = new();
    public IReadOnlyList<ServerEndpointConfig> ServerEndpoints { get; init; } = [];
}
