using System.Text.Json;
using StorageService.Api.Models;

namespace StorageService.Api.Services;

public interface IDbCommandParser
{
    DbCommandParseResult Parse(string rawJson);
}

public sealed class DbCommandParseResult
{
    public DbCommand? Command { get; init; }
    public DbResponse? Error { get; init; }
    public bool IsSuccess => Command is not null;
}

public sealed class DbCommandParser : IDbCommandParser
{
    public DbCommandParseResult Parse(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return Fail(-100, "invalid json");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(rawJson);
        }
        catch (JsonException)
        {
            return Fail(-100, "invalid json");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Fail(-100, "invalid json");
            }

            var root = document.RootElement;
            var command = new DbCommand
            {
                DbName = GetString(root, "db_name", "dbName"),
                Operation = GetString(root, "operation", "op").ToLowerInvariant(),
                OpMode = GetString(root, "op_mode", "opMode").ToLowerInvariant(),
                Key = GetString(root, "key"),
                Value = GetValueAsStorageString(root, "value"),
                UniqueKey = GetString(root, "uniqueKey"),
                KeyList = GetValueAsStorageString(root, "key_list", "keyList"),
                IsBatch = GetBool(root, "is_batch", "isBatch"),
                Limit = GetNullableInt(root, "limit")
            };

            return new DbCommandParseResult { Command = command };
        }
    }

    private static DbCommandParseResult Fail(int code, string msg, string uniqueKey = "") =>
        new() { Error = DbResponse.Error(code, msg, uniqueKey) };

    private static string GetString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var value))
            {
                continue;
            }

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? "",
                JsonValueKind.Null or JsonValueKind.Undefined => "",
                _ => value.GetRawText()
            };
        }

        return "";
    }

    private static string GetValueAsStorageString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var value))
            {
                continue;
            }

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? "",
                JsonValueKind.Null or JsonValueKind.Undefined => "",
                _ => value.GetRawText()
            };
        }

        return "";
    }

    private static bool GetBool(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var value))
            {
                continue;
            }

            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => bool.TryParse(value.GetString(), out var parsed) && parsed,
                JsonValueKind.Number => value.TryGetInt32(out var number) && number != 0,
                _ => false
            };
        }

        return false;
    }

    private static int? GetNullableInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number))
        {
            return number;
        }

        return null;
    }
}
