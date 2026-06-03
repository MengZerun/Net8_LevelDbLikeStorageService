namespace StorageService.Api.Models;

public sealed class ZmqIncomingMessage
{
    public string EndpointId { get; init; } = "";
    public string Mode { get; init; } = "";
    public string Type { get; init; } = "";
    public string Topic { get; init; } = "";
    public string Identity { get; init; } = "";
    public byte[] IdentityBytes { get; init; } = [];
    public string Payload { get; init; } = "";
    public bool UseEmptyDelimiter { get; init; }
    public IReadOnlyList<string> Frames { get; init; } = [];
}

public sealed class ZmqOutgoingMessage
{
    public string EndpointId { get; init; } = "";
    public string Mode { get; init; } = "";
    public string Topic { get; init; } = "";
    public string Identity { get; init; } = "";
    public byte[] IdentityBytes { get; init; } = [];
    public string Payload { get; init; } = "";
    public bool UseEmptyDelimiter { get; init; }
}
