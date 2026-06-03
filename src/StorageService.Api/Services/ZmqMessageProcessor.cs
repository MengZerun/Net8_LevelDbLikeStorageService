using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using log4net;
using StorageService.Api.Models;

namespace StorageService.Api.Services;

public interface IZmqMessageProcessor
{
    Task<DbResponse> ProcessAsync(ZmqIncomingMessage message, CancellationToken ct);
    string SerializeResponse(DbResponse response);
}

public sealed class ZmqMessageProcessor : IZmqMessageProcessor
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(ZmqMessageProcessor));
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    private readonly IDbCommandParser _parser;
    private readonly IDbCommandHandler _handler;

    public ZmqMessageProcessor(IDbCommandParser parser, IDbCommandHandler handler)
    {
        _parser = parser;
        _handler = handler;
    }

    public async Task<DbResponse> ProcessAsync(ZmqIncomingMessage message, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var parsed = _parser.Parse(message.Payload);
        var response = parsed.Error ?? await _handler.HandleAsync(parsed.Command!, ct);
        stopwatch.Stop();

        Log.Info($"[ZMQ-DB] endpoint={message.EndpointId} uniqueKey={response.UniqueKey} db={parsed.Command?.DbName ?? ""} op={parsed.Command?.Operation ?? ""} mode={parsed.Command?.OpMode ?? ""} resultCode={response.ResultCode} elapsedMs={stopwatch.ElapsedMilliseconds}");
        return response;
    }

    public string SerializeResponse(DbResponse response) => JsonSerializer.Serialize(response, JsonOptions);
}
