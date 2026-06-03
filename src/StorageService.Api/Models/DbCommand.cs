using System.Text.Json.Serialization;

namespace StorageService.Api.Models;

public sealed class DbCommand
{
    public string DbName { get; set; } = "";
    public string Operation { get; set; } = "";
    public string OpMode { get; set; } = "";
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public string UniqueKey { get; set; } = "";
    public string KeyList { get; set; } = "";
    public bool IsBatch { get; set; }
    public int? Limit { get; set; }
}

public sealed class DbResponse
{
    [JsonPropertyName("resultCode")]
    public int ResultCode { get; set; }

    [JsonPropertyName("msg")]
    public string Msg { get; set; } = "";

    [JsonPropertyName("value")]
    public string Value { get; set; } = "";

    [JsonPropertyName("uniqueKey")]
    public string UniqueKey { get; set; } = "";

    public static DbResponse Success(string uniqueKey, string value = "", string msg = "success") =>
        new() { ResultCode = 0, Msg = msg, Value = value, UniqueKey = uniqueKey };

    public static DbResponse Error(int code, string msg, string uniqueKey = "") =>
        new() { ResultCode = code, Msg = msg, Value = "", UniqueKey = uniqueKey };
}
