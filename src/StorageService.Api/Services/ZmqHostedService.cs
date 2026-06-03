using System.Diagnostics;
using System.Text;
using System.Text.Json;
using log4net;
using NetMQ;
using NetMQ.Sockets;
using StorageService.Api.Models;

namespace StorageService.Api.Services;

public sealed class ZmqHostedService : BackgroundService
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(ZmqHostedService));
    private readonly RuntimeConfig _config;
    private readonly IServiceProvider _services;
    private readonly List<ZmqEndpointRuntime> _endpoints = [];
    private NetMQPoller? _poller;
    private NetMQQueue<ZmqOutgoingMessage>? _sendQueue;
    private int _stopped;

    public ZmqHostedService(RuntimeConfig config, IServiceProvider services)
    {
        _config = config;
        _services = services;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.Db.ZmqEnabled)
        {
            Log.Info("[ZMQ] disabled by configuration.");
            return;
        }

        try
        {
            StartPoller(stoppingToken);
        }
        catch (Exception ex)
        {
            Log.Error("[ZMQ] startup failed.", ex);
            if (_config.Db.ZmqStrictStartup)
            {
                throw;
            }
        }

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 1)
        {
            return Task.CompletedTask;
        }

        Log.Info("[ZMQ] stopping.");
        try
        {
            _poller?.StopAsync();
            var waitUntil = DateTime.UtcNow.AddSeconds(2);
            while (_poller?.IsRunning == true && DateTime.UtcNow < waitUntil)
            {
                Thread.Sleep(20);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("[ZMQ] poller stop failed.", ex);
        }

        foreach (var endpoint in _endpoints)
        {
            try
            {
                Log.Info($"[ZMQ] endpoint={endpoint.Id} disposing.");
                _poller?.Remove(endpoint.Socket);
                endpoint.Socket.Dispose();
            }
            catch (Exception ex)
            {
                Log.Warn($"[ZMQ] endpoint={endpoint.Id} dispose failed.", ex);
            }
        }

        try
        {
            if (_sendQueue is not null)
            {
                _poller?.Remove(_sendQueue);
                _sendQueue.Dispose();
            }

            _poller?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warn("[ZMQ] poller dispose failed.", ex);
        }

        _endpoints.Clear();
        return Task.CompletedTask;
    }

    internal static string BuildAddress(ServerEndpointConfig endpoint, bool bind)
    {
        var protocol = Normalize(endpoint.Protocol, "tcp");
        var ip = string.IsNullOrWhiteSpace(endpoint.Ip) ? "*" : endpoint.Ip.Trim();
        var port = endpoint.Port?.Trim() ?? "";

        if (protocol == "inproc")
        {
            var name = $"{ip}_{port}".Replace("://", "_").Replace(":", "_").Replace("/", "_");
            return $"inproc://{name}";
        }

        if (protocol != "tcp")
        {
            throw new NotSupportedException($"unsupported zmq protocol: {endpoint.Protocol}");
        }

        if (!bind && ip == "*")
        {
            ip = "127.0.0.1";
        }

        return $"tcp://{ip}:{port}";
    }

    internal static bool ShouldBind(ServerEndpointConfig endpoint)
    {
        if (endpoint.Bind.HasValue)
        {
            return endpoint.Bind.Value;
        }

        var mode = Normalize(endpoint.Mode, "");
        var type = Normalize(endpoint.Type, "");
        return (mode, type) switch
        {
            ("pub", "send") => true,
            ("sub", "recv") => false,
            ("router", "recv") => true,
            ("dealer", _) => false,
            ("rep", "recv") => true,
            ("req", "send") => false,
            ("push", "send") => true,
            ("pull", "recv") => true,
            _ => type == "send"
        };
    }

    private void StartPoller(CancellationToken stoppingToken)
    {
        _poller = new NetMQPoller();
        _sendQueue = new NetMQQueue<ZmqOutgoingMessage>(1024);
        _sendQueue.ReceiveReady += (_, args) =>
        {
            while (args.Queue.TryDequeue(out var outgoing, TimeSpan.Zero))
            {
                if (outgoing is not null)
                {
                    SendOnPoller(outgoing);
                }
            }
        };
        _poller.Add(_sendQueue);

        foreach (var endpoint in _config.ServerEndpoints.Where(e => e.Enabled))
        {
            try
            {
                var runtime = CreateEndpoint(endpoint, stoppingToken);
                _endpoints.Add(runtime);
                _poller.Add(runtime.Socket);
            }
            catch (Exception ex)
            {
                Log.Error($"[ZMQ] endpoint={GetEndpointId(endpoint)} initialization failed.", ex);
                if (_config.Db.ZmqStrictStartup)
                {
                    throw;
                }
            }
        }

        if (_endpoints.Count == 0)
        {
            Log.Info("[ZMQ] no enabled endpoints.");
            return;
        }

        _poller.RunAsync("StorageService-ZMQ");
        Log.Info($"[ZMQ] poller started. endpointCount={_endpoints.Count}");
    }

    private ZmqEndpointRuntime CreateEndpoint(ServerEndpointConfig endpoint, CancellationToken stoppingToken)
    {
        var mode = Normalize(endpoint.Mode, "");
        var type = Normalize(endpoint.Type, "");
        var bind = ShouldBind(endpoint);
        var address = BuildAddress(endpoint, bind);
        var id = GetEndpointId(endpoint);
        var socket = CreateSocket(mode);
        socket.Options.Linger = TimeSpan.Zero;

        if (mode == "dealer")
        {
            var identity = endpoint.ServerId ?? endpoint.Topic.FirstOrDefault() ?? endpoint.Id;
            if (!string.IsNullOrWhiteSpace(identity))
            {
                socket.Options.Identity = Encoding.UTF8.GetBytes(identity);
            }
        }

        if (bind)
        {
            socket.Bind(address);
        }
        else
        {
            socket.Connect(address);
        }

        var runtime = new ZmqEndpointRuntime(id, mode, type, bind, address, endpoint.Topic ?? [], socket);

        if (mode == "sub" && socket is SubscriberSocket sub)
        {
            var filters = endpoint.Filter ?? [];
            if (filters.Length == 0)
            {
                sub.Subscribe("");
                Log.Info($"[ZMQ] endpoint={id} sub subscribe=<all>");
            }
            else
            {
                foreach (var filter in filters)
                {
                    sub.Subscribe(filter);
                    Log.Info($"[ZMQ] endpoint={id} sub subscribe={filter}");
                }
            }
        }

        if (IsReceiveEndpoint(mode, type))
        {
            socket.ReceiveReady += (_, args) => ReceiveOnPoller(runtime, args.Socket, stoppingToken);
        }

        Log.Info($"[ZmqEndpoint] id={id} mode={mode} type={type} bind={bind} address={address} started");
        if (mode == "pub")
        {
            Log.Info($"[ZMQ] endpoint={id} pub topics={string.Join(",", runtime.Topics.DefaultIfEmpty("dbrep"))}");
        }

        return runtime;
    }

    private static NetMQSocket CreateSocket(string mode) => mode switch
    {
        "router" => new RouterSocket(),
        "dealer" => new DealerSocket(),
        "pub" => new PublisherSocket(),
        "sub" => new SubscriberSocket(),
        "rep" => new ResponseSocket(),
        "req" => new RequestSocket(),
        "push" => new PushSocket(),
        "pull" => new PullSocket(),
        _ => throw new NotSupportedException($"unsupported zmq mode: {mode}")
    };

    private static bool IsReceiveEndpoint(string mode, string type) =>
        type == "recv" || mode is "router" or "sub" or "dealer" or "rep" or "pull";

    private void ReceiveOnPoller(ZmqEndpointRuntime endpoint, NetMQSocket socket, CancellationToken stoppingToken)
    {
        try
        {
            var message = socket.ReceiveMultipartMessage();
            var incoming = ParseIncoming(endpoint, message);
            Log.Info($"[ZMQ-RECV] endpoint={incoming.EndpointId} mode={incoming.Mode} topic={incoming.Topic} identity={incoming.Identity} bytes={Encoding.UTF8.GetByteCount(incoming.Payload)} frameCount={message.FrameCount}");
            Log.Debug($"[ZMQ-RECV-PAYLOAD] endpoint={incoming.EndpointId} payload={Truncate(incoming.Payload, 512)}");

            _ = Task.Run(async () =>
            {
                using var scope = _services.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<IZmqMessageProcessor>();
                var stopwatch = Stopwatch.StartNew();
                var response = await processor.ProcessAsync(incoming, stoppingToken);
                stopwatch.Stop();
                var payload = processor.SerializeResponse(response);
                EnqueueReply(endpoint, incoming, payload);
                Log.Info($"[ZMQ-PROCESS] endpoint={incoming.EndpointId} resultCode={response.ResultCode} elapsedMs={stopwatch.ElapsedMilliseconds}");
            }, stoppingToken);
        }
        catch (Exception ex)
        {
            Log.Error($"[ZMQ] endpoint={endpoint.Id} socket receive failed.", ex);
        }
    }

    private static ZmqIncomingMessage ParseIncoming(ZmqEndpointRuntime endpoint, NetMQMessage message)
    {
        var frames = Enumerable.Range(0, message.FrameCount)
            .Select(index => message[index].ConvertToString(Encoding.UTF8))
            .ToArray();
        var topic = "";
        var identity = "";
        var identityBytes = Array.Empty<byte>();
        var payload = frames.Length > 0 ? frames[^1] : "";
        var useEmptyDelimiter = false;

        if (endpoint.Mode == "sub")
        {
            topic = frames.Length >= 2 ? frames[0] : "";
            payload = frames.Length >= 2 ? frames[1] : payload;
        }
        else if (endpoint.Mode == "router")
        {
            identity = frames.Length >= 1 ? frames[0] : "";
            identityBytes = message.FrameCount >= 1 ? message[0].ToByteArray() : [];
            useEmptyDelimiter = frames.Length >= 3 && string.IsNullOrEmpty(frames[1]);
            payload = useEmptyDelimiter ? frames[2] : frames.Length >= 2 ? frames[1] : "";
        }

        return new ZmqIncomingMessage
        {
            EndpointId = endpoint.Id,
            Mode = endpoint.Mode,
            Type = endpoint.Type,
            Topic = topic,
            Identity = identity,
            IdentityBytes = identityBytes,
            Payload = payload,
            UseEmptyDelimiter = useEmptyDelimiter,
            Frames = frames.ToArray()
        };
    }

    private void EnqueueReply(ZmqEndpointRuntime sourceEndpoint, ZmqIncomingMessage incoming, string payload)
    {
        if (_sendQueue is null)
        {
            return;
        }

        if (incoming.Mode == "router")
        {
            _sendQueue.Enqueue(new ZmqOutgoingMessage
            {
                EndpointId = sourceEndpoint.Id,
                Mode = "router",
                Identity = incoming.Identity,
                IdentityBytes = incoming.IdentityBytes,
                Payload = payload,
                UseEmptyDelimiter = incoming.UseEmptyDelimiter
            });
            return;
        }

        if (incoming.Mode == "dealer")
        {
            _sendQueue.Enqueue(new ZmqOutgoingMessage
            {
                EndpointId = sourceEndpoint.Id,
                Mode = "dealer",
                Payload = payload
            });
            return;
        }

        if (incoming.Mode == "sub")
        {
            var pubEndpoint = _endpoints.FirstOrDefault(e => e.Mode == "pub" && e.Type == "send");
            if (pubEndpoint is null)
            {
                Log.Warn($"[ZMQ-SEND] no pub endpoint available for source={incoming.EndpointId}");
                return;
            }

            _sendQueue.Enqueue(new ZmqOutgoingMessage
            {
                EndpointId = pubEndpoint.Id,
                Mode = "pub",
                Topic = ExtractReplyTopic(incoming.Payload) ?? pubEndpoint.Topics.FirstOrDefault() ?? "dbrep",
                Payload = payload
            });
        }
    }

    private void SendOnPoller(ZmqOutgoingMessage outgoing)
    {
        var endpoint = _endpoints.FirstOrDefault(e => e.Id == outgoing.EndpointId);
        if (endpoint is null)
        {
            Log.Warn($"[ZMQ-SEND] endpoint not found: {outgoing.EndpointId}");
            return;
        }

        try
        {
            switch (outgoing.Mode)
            {
                case "router":
                    var router = endpoint.Socket;
                    if (outgoing.UseEmptyDelimiter)
                    {
                        router.SendMoreFrame(outgoing.IdentityBytes).SendMoreFrame("").SendFrame(outgoing.Payload);
                    }
                    else
                    {
                        router.SendMoreFrame(outgoing.IdentityBytes).SendFrame(outgoing.Payload);
                    }
                    Log.Info($"[ZMQ-SEND] endpoint={endpoint.Id} router identity={outgoing.Identity} bytes={Encoding.UTF8.GetByteCount(outgoing.Payload)}");
                    break;
                case "pub":
                    endpoint.Socket.SendMoreFrame(outgoing.Topic).SendFrame(outgoing.Payload);
                    Log.Info($"[ZMQ-SEND] endpoint={endpoint.Id} topic={outgoing.Topic} bytes={Encoding.UTF8.GetByteCount(outgoing.Payload)}");
                    break;
                case "dealer":
                    endpoint.Socket.SendFrame(outgoing.Payload);
                    Log.Info($"[ZMQ-SEND] endpoint={endpoint.Id} dealer bytes={Encoding.UTF8.GetByteCount(outgoing.Payload)}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[ZMQ-SEND] endpoint={endpoint.Id} send failed.", ex);
        }
    }

    private static string? ExtractReplyTopic(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.TryGetProperty("replyTopic", out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string GetEndpointId(ServerEndpointConfig endpoint) =>
        endpoint.Id ?? $"{Normalize(endpoint.Mode, "socket")}_{Normalize(endpoint.Type, "endpoint")}_{endpoint.Port}";

    private static string Normalize(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToLowerInvariant();

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "...";

    private sealed record ZmqEndpointRuntime(
        string Id,
        string Mode,
        string Type,
        bool Bind,
        string Address,
        IReadOnlyList<string> Topics,
        NetMQSocket Socket);
}
