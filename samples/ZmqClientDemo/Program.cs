using NetMQ;
using NetMQ.Sockets;

var command = """
{
  "db_name": "tray",
  "operation": "put",
  "op_mode": "all_ow",
  "key": "zmq-demo-k1",
  "value": "{\"a\":1}",
  "uniqueKey": "zmq-demo-u1",
  "key_list": "",
  "is_batch": "false",
  "replyTopic": "fromDb_to_station2"
}
""";

Console.WriteLine("ROUTER/DEALER demo...");
using (var dealer = new DealerSocket())
{
    dealer.Options.Identity = "clientA"u8.ToArray();
    dealer.Connect("tcp://127.0.0.1:9199");
    dealer.SendFrame(command);
    var reply = dealer.ReceiveFrameString();
    Console.WriteLine(reply);
}

Console.WriteLine("PUB/SUB demo...");
using (var subscriber = new SubscriberSocket())
using (var publisher = new PublisherSocket())
{
    subscriber.Connect("tcp://127.0.0.1:9202");
    subscriber.Subscribe("fromDb_to_station2");

    publisher.Bind("tcp://*:9201");
    Thread.Sleep(800);

    publisher.SendMoreFrame("from_station2_toDb").SendFrame(command.Replace("zmq-demo-u1", "zmq-demo-u2"));

    var topic = subscriber.ReceiveFrameString();
    var reply = subscriber.ReceiveFrameString();
    Console.WriteLine($"topic={topic}");
    Console.WriteLine(reply);
}

NetMQConfig.Cleanup(false);
