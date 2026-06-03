using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using NetMQ;
using NetMQ.Sockets;

namespace StorageService.Tests;

[CollectionDefinition("Zmq", DisableParallelization = true)]
public sealed class ZmqCollection;

[Collection("Zmq")]
public sealed class ZmqCompatibilityTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "Net8ZmqCompatibilityTests", Guid.NewGuid().ToString("N"));

    [Fact]
    [Trait("Category", "Zmq")]
    public async Task RouterProcessesJsonAndInvalidJsonAndDelimiterFrames()
    {
        var ports = AllocatePorts(3);
        var configPath = CreateConfig(_tempRoot, ports.Router, ports.Sub, ports.Pub, ["from_station2_toDb"]);
        using var factory = CreateFactory(configPath);
        _ = factory.CreateClient();

        using var dealer = new DealerSocket();
        dealer.Options.Identity = "client-router"u8.ToArray();
        dealer.Connect($"tcp://127.0.0.1:{ports.Router}");
        Thread.Sleep(300);

        dealer.SendFrame(Command("router-k1", "router-u1"));
        var reply = dealer.ReceiveFrameString(TimeSpan.FromSeconds(5));
        Assert.Equal(0, ResultCode(reply));

        dealer.SendFrame("Hello");
        var invalidReply = dealer.ReceiveFrameString(TimeSpan.FromSeconds(5));
        Assert.Equal(-100, ResultCode(invalidReply));

        dealer.SendMoreFrame("").SendFrame(Command("router-k2", "router-u2"));
        var delimiterReplyFrames = dealer.ReceiveMultipartStrings(2);
        Assert.Equal("", delimiterReplyFrames[0]);
        Assert.Equal(0, ResultCode(delimiterReplyFrames[1]));
    }

    [Fact]
    [Trait("Category", "Zmq")]
    public void PubSubProcessesSubscribedTopicAndPublishesResponse()
    {
        var ports = AllocatePorts(3);
        var configPath = CreateConfig(_tempRoot, ports.Router, ports.Sub, ports.Pub, ["from_station2_toDb", "dbreq"]);

        using var requestPublisher = new PublisherSocket();
        requestPublisher.Bind($"tcp://*:{ports.Sub}");

        using var factory = CreateFactory(configPath);
        _ = factory.CreateClient();

        using var responseSubscriber = new SubscriberSocket();
        responseSubscriber.Connect($"tcp://127.0.0.1:{ports.Pub}");
        responseSubscriber.Subscribe("fromDb_to_station2");
        Thread.Sleep(900);

        requestPublisher.SendMoreFrame("dbreq").SendFrame(Command("sub-k1", "sub-u1"));

        var frames = responseSubscriber.ReceiveMultipartStrings(2, TimeSpan.FromSeconds(5));
        Assert.Equal("fromDb_to_station2", frames[0]);
        Assert.Equal(0, ResultCode(frames[1]));
    }

    [Fact]
    [Trait("Category", "Zmq")]
    public void EmptySubFilterSubscribesToAllTopics()
    {
        var ports = AllocatePorts(3);
        var configPath = CreateConfig(_tempRoot, ports.Router, ports.Sub, ports.Pub, []);

        using var requestPublisher = new PublisherSocket();
        requestPublisher.Bind($"tcp://*:{ports.Sub}");

        using var factory = CreateFactory(configPath);
        _ = factory.CreateClient();

        using var responseSubscriber = new SubscriberSocket();
        responseSubscriber.Connect($"tcp://127.0.0.1:{ports.Pub}");
        responseSubscriber.Subscribe("fromDb_to_station2");
        Thread.Sleep(900);

        requestPublisher.SendMoreFrame("any_topic").SendFrame(Command("sub-any-k1", "sub-any-u1"));

        var frames = responseSubscriber.ReceiveMultipartStrings(2, TimeSpan.FromSeconds(5));
        Assert.Equal("fromDb_to_station2", frames[0]);
        Assert.Equal(0, ResultCode(frames[1]));
    }

    [Fact]
    [Trait("Category", "Zmq")]
    public async Task ZmqStartupFailureDoesNotBreakHttpWhenStrictStartupIsFalse()
    {
        var occupiedPort = AllocatePorts(1).Router;
        using var blocker = new RouterSocket();
        blocker.Bind($"tcp://*:{occupiedPort}");

        var ports = AllocatePorts(2);
        var configPath = CreateConfig(_tempRoot, occupiedPort, ports.Sub, ports.Pub, ["dbreq"], strictStartup: false);
        using var factory = CreateFactory(configPath);
        var client = factory.CreateClient();

        var health = await client.GetStringAsync("/health");
        Assert.Contains("\"status\":\"ok\"", health);
    }

    [Fact]
    [Trait("Category", "Zmq")]
    public void RouterPortIsReleasedAfterHostStops()
    {
        var ports = AllocatePorts(3);
        var configPath = CreateConfig(_tempRoot, ports.Router, ports.Sub, ports.Pub, ["dbreq"]);

        using (var factory = CreateFactory(configPath))
        {
            _ = factory.CreateClient();
            using var dealer = new DealerSocket();
            dealer.Connect($"tcp://127.0.0.1:{ports.Router}");
            Thread.Sleep(300);
            dealer.SendFrame(Command("release-k1", "release-u1"));
            Assert.Equal(0, ResultCode(dealer.ReceiveFrameString(TimeSpan.FromSeconds(5))));
        }

        Thread.Sleep(500);
        using var rebinder = new RouterSocket();
        rebinder.Bind($"tcp://*:{ports.Router}");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("STORAGE_CONFIG_PATH", null);
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
        }
    }

    private static WebApplicationFactory<Program> CreateFactory(string configPath)
    {
        Environment.SetEnvironmentVariable("STORAGE_CONFIG_PATH", configPath);
        return new WebApplicationFactory<Program>();
    }

    private static string CreateConfig(string root, int routerPort, int subPort, int pubPort, string[] filters, bool strictStartup = false)
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
        File.WriteAllText(Path.Combine(configDir, "db_config.json"), JsonSerializer.Serialize(new
        {
            root_path = Path.Combine(root, "db"),
            http_port = 9877,
            diskCheckSpace = 0,
            engine = "file",
            maxRequestBodyMb = 10,
            zmq_enabled = true,
            zmq_strict_startup = strictStartup
        }));
        File.WriteAllText(Path.Combine(configDir, "db.json"), JsonSerializer.Serialize(new
        {
            tray = new { path = Path.Combine(root, "db", "tray"), status = "active", version = "0.0.0.1" }
        }));
        File.WriteAllText(Path.Combine(configDir, "log_config.json"), JsonSerializer.Serialize(new
        {
            log_path = Path.Combine(root, "log"),
            log_clean_days = 1,
            min_log_level = 1
        }));
        File.WriteAllText(Path.Combine(configDir, "server_config.json"), JsonSerializer.Serialize(new object[]
        {
            new
            {
                id = "test_router",
                filter = Array.Empty<string>(),
                ip = "*",
                mode = "router",
                port = routerPort.ToString(),
                protocol = "tcp",
                topic = Array.Empty<string>(),
                type = "recv",
                enabled = true
            },
            new
            {
                id = "test_sub",
                filter = filters,
                ip = "127.0.0.1",
                mode = "sub",
                port = subPort.ToString(),
                protocol = "tcp",
                topic = Array.Empty<string>(),
                type = "recv",
                enabled = true
            },
            new
            {
                id = "test_pub",
                filter = Array.Empty<string>(),
                ip = "*",
                mode = "pub",
                port = pubPort.ToString(),
                protocol = "tcp",
                topic = new[] { "fromDb_to_station2", "dbrep" },
                type = "send",
                enabled = true
            }
        }));
        File.WriteAllText(Path.Combine(configDir, "get_all_file.json"), "{}");
        return Path.Combine(configDir, "config.json");
    }

    private static string Command(string key, string uniqueKey) =>
        $$"""{"db_name":"tray","operation":"put","op_mode":"all_ow","key":"{{key}}","value":"{\"a\":1}","uniqueKey":"{{uniqueKey}}","key_list":"","is_batch":"false","replyTopic":"fromDb_to_station2"}""";

    private static int ResultCode(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("resultCode").GetInt32();
    }

    private static (int Router, int Sub, int Pub) AllocatePorts(int count)
    {
        var ports = new List<int>();
        for (var i = 0; i < count; i++)
        {
            ports.Add(GetFreeTcpPort());
        }

        while (ports.Count < 3)
        {
            ports.Add(GetFreeTcpPort());
        }

        return (ports[0], ports[1], ports[2]);
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

internal static class NetMqTestExtensions
{
    public static string ReceiveFrameString(this IReceivingSocket socket, TimeSpan timeout)
    {
        Assert.True(socket.TryReceiveFrameString(timeout, out var frame), "Timed out waiting for ZMQ frame.");
        return frame;
    }

    public static List<string> ReceiveMultipartStrings(this IReceivingSocket socket, int expectedFrameCount, TimeSpan timeout)
    {
        var frames = new List<string>();
        Assert.True(socket.TryReceiveMultipartStrings(timeout, ref frames, expectedFrameCount), "Timed out waiting for ZMQ multipart message.");
        return frames;
    }
}
