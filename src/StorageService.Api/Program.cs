using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using log4net;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Hosting.WindowsServices;
using StorageService.Api.Logging;
using StorageService.Api.Models;
using StorageService.Api.Services;
using StorageService.Api.Storage;

var configLoader = new ConfigLoader();
var appBaseDirectory = AppContext.BaseDirectory;
var bootstrapConfig = configLoader.Load(args, appBaseDirectory);
Log4NetSetup.Configure(bootstrapConfig.Log, appBaseDirectory);
var log = LogManager.GetLogger(typeof(Program));

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService();
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = bootstrapConfig.Db.MaxRequestBodyMb * 1024 * 1024;
});

if (!args.Any(arg => arg.Equals("--urls", StringComparison.OrdinalIgnoreCase)))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{bootstrapConfig.Db.HttpPort}");
}

builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
    options.SerializerOptions.WriteIndented = false;
});

builder.Services.AddSingleton(bootstrapConfig);
builder.Services.AddSingleton<IConfigLoader, ConfigLoader>();
builder.Services.AddSingleton<IDbCommandParser, DbCommandParser>();
builder.Services.AddSingleton<IKvEngine>(_ => bootstrapConfig.Db.Engine.ToLowerInvariant() switch
{
    "leveldb" => new LevelDbKvEngine(),
    "file" => new FileKvEngine(),
    "rocksdb" => new FileKvEngine(),
    _ => new FileKvEngine()
});
builder.Services.AddSingleton<IDatabaseManager, DatabaseManager>();
builder.Services.AddSingleton<IDiskSpaceChecker>(_ => new DiskSpaceChecker(bootstrapConfig.Db.DiskCheckSpace));
builder.Services.AddSingleton<IDbCommandHandler, DbCommandHandler>();
builder.Services.AddSingleton<IZmqMessageProcessor, ZmqMessageProcessor>();
if (bootstrapConfig.Db.ZmqEnabled)
{
    builder.Services.AddHostedService<ZmqHostedService>();
}

var app = builder.Build();

var manager = app.Services.GetRequiredService<IDatabaseManager>();
await manager.InitializeAsync(app.Lifetime.ApplicationStopping);
app.Lifetime.ApplicationStopping.Register(() =>
{
    try
    {
        manager.CloseAllAsync().GetAwaiter().GetResult();
    }
    catch (Exception ex)
    {
        log.Error("Close databases failed.", ex);
    }
});

var responseJsonOptions = new JsonSerializerOptions
{
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    WriteIndented = false
};

async Task<IResult> HandleDbCommand(HttpContext context, IDbCommandParser parser, IDbCommandHandler handler)
{
    var stopwatch = Stopwatch.StartNew();
    string rawBody;
    using (var reader = new StreamReader(context.Request.Body))
    {
        rawBody = await reader.ReadToEndAsync(context.RequestAborted);
    }

    var parsed = parser.Parse(rawBody);
    DbCommand? command = parsed.Command;
    var response = parsed.Error ?? await handler.HandleAsync(command!, context.RequestAborted);
    stopwatch.Stop();

    log.Info($"[DbCommand] uniqueKey={response.UniqueKey} db={command?.DbName ?? ""} op={command?.Operation ?? ""} mode={command?.OpMode ?? ""} key={command?.Key ?? ""} bodyBytes={context.Request.ContentLength ?? rawBody.Length} elapsedMs={stopwatch.ElapsedMilliseconds} resultCode={response.ResultCode}");
    if (response.ResultCode < 0)
    {
        log.Warn($"[DbCommandError] uniqueKey={response.UniqueKey} msg={response.Msg}");
    }

    return Results.Json(response, responseJsonOptions);
}

app.MapPost("/", HandleDbCommand);
app.MapPost("/db", HandleDbCommand);
app.MapPost("/api/db", HandleDbCommand);
app.MapPost("/api/storage", HandleDbCommand);

app.MapGet("/health", (IDatabaseManager dbManager) => Results.Json(new
{
    status = "ok",
    service = "Net8_LevelDbLikeStorageService",
    version = "1.0.0",
    dbCount = dbManager.OpenedDatabases.Count,
    time = DateTimeOffset.Now
}, responseJsonOptions));

app.MapGet("/api/dbs", (IDatabaseManager dbManager) => Results.Json(new
{
    resultCode = 0,
    value = dbManager.Descriptors.Select(pair => new
    {
        name = pair.Key,
        path = pair.Value.Path,
        status = pair.Value.Status,
        opened = dbManager.OpenedDatabases.ContainsKey(pair.Key)
    }).ToArray()
}, responseJsonOptions));

if (bootstrapConfig.Db.EnableAdminApi)
{
    app.MapPost("/api/admin/shutdown", (IHostApplicationLifetime lifetime) =>
    {
        lifetime.StopApplication();
        return Results.Json(DbResponse.Success("shutdown", "", "shutdown requested"), responseJsonOptions);
    });
}

log.Info($"Net8_LevelDbLikeStorageService starting. config={bootstrapConfig.ConfigPath} port={bootstrapConfig.Db.HttpPort} engine={bootstrapConfig.Db.Engine}");
await app.RunAsync();

public partial class Program;
