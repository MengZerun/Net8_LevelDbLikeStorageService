using System.Text.Encodings.Web;
using System.Text.Json;
using StorageService.Api.Models;
using StorageService.Api.Storage;

namespace StorageService.Api.Services;

public interface IDbCommandHandler
{
    Task<DbResponse> HandleAsync(DbCommand command, CancellationToken ct);
}

public sealed class DbCommandHandler : IDbCommandHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    private readonly IDatabaseManager _databaseManager;
    private readonly IDiskSpaceChecker _diskSpaceChecker;

    public DbCommandHandler(IDatabaseManager databaseManager, IDiskSpaceChecker diskSpaceChecker)
    {
        _databaseManager = databaseManager;
        _diskSpaceChecker = diskSpaceChecker;
    }

    public async Task<DbResponse> HandleAsync(DbCommand command, CancellationToken ct)
    {
        var validation = Validate(command);
        if (validation is not null)
        {
            return validation;
        }

        if (!_databaseManager.TryGetDatabase(command.DbName, out var database, out var dbError, command.UniqueKey))
        {
            return dbError!;
        }

        try
        {
            return command.Operation switch
            {
                "put" => await PutAsync(database!, command, ct),
                "get" => await GetAsync(database!, command, ct),
                "delete" => await DeleteAsync(database!, command, ct),
                "list" => await ListAsync(database!, command, ct),
                _ => DbResponse.Error(-301, $"unsupported operation: {command.Operation}", command.UniqueKey)
            };
        }
        catch (InvalidOperationException ex)
        {
            return DbResponse.Error(-302, ex.Message, command.UniqueKey);
        }
        catch (JsonException ex)
        {
            return DbResponse.Error(-500, $"storage engine error: json parse failed: {ex.Message}", command.UniqueKey);
        }
        catch (Exception ex)
        {
            return DbResponse.Error(-500, $"storage engine error: {ex.Message}", command.UniqueKey);
        }
    }

    private async Task<DbResponse> PutAsync(IKvDatabase database, DbCommand command, CancellationToken ct)
    {
        if (command.OpMode is not ("all_ow" or "all" or "ap" or "kv"))
        {
            throw new InvalidOperationException($"unsupported op_mode: {command.OpMode}");
        }

        if (!_diskSpaceChecker.HasEnoughFreeSpace(database, out var diskMessage))
        {
            return DbResponse.Error(-601, diskMessage.StartsWith("disk free space is lower", StringComparison.Ordinal)
                ? "disk free space is lower than threshold"
                : diskMessage, command.UniqueKey);
        }

        if (command.IsBatch || command.OpMode == "kv")
        {
            var pairs = ParseBatchValues(command.Value);
            if (pairs.Count == 0)
            {
                return DbResponse.Error(-101, "missing required field: value", command.UniqueKey);
            }

            await database.PutManyAsync(pairs, ct);
            return DbResponse.Success(command.UniqueKey);
        }

        if (command.OpMode == "ap")
        {
            var existing = await database.GetAsync(command.Key, ct);
            var appended = AppendValue(existing, command.Value);
            await database.PutAsync(command.Key, appended, ct);
            return DbResponse.Success(command.UniqueKey);
        }

        await database.PutAsync(command.Key, command.Value, ct);
        return DbResponse.Success(command.UniqueKey);
    }

    private async Task<DbResponse> GetAsync(IKvDatabase database, DbCommand command, CancellationToken ct)
    {
        if (command.OpMode is not ("all" or "all_ow" or "last"))
        {
            throw new InvalidOperationException($"unsupported op_mode: {command.OpMode}");
        }

        var value = await database.GetAsync(command.Key, ct);
        if (value is not null)
        {
            return DbResponse.Success(command.UniqueKey, value);
        }

        if (command.OpMode == "last")
        {
            var values = await database.ListKvsByPrefixAsync(command.Key, command.Limit, ct);
            if (values.Count > 0)
            {
                return DbResponse.Success(command.UniqueKey, values[^1].Value);
            }
        }

        return DbResponse.Success(command.UniqueKey, "", "not found");
    }

    private async Task<DbResponse> DeleteAsync(IKvDatabase database, DbCommand command, CancellationToken ct)
    {
        if (command.OpMode is not ("all" or "all_ow"))
        {
            throw new InvalidOperationException($"unsupported op_mode: {command.OpMode}");
        }

        await database.DeleteAsync(command.Key, ct);
        return DbResponse.Success(command.UniqueKey);
    }

    private async Task<DbResponse> ListAsync(IKvDatabase database, DbCommand command, CancellationToken ct)
    {
        if (command.OpMode == "prefix_keys")
        {
            var keys = await database.ListKeysByPrefixAsync(command.Key, command.Limit, ct);
            return DbResponse.Success(command.UniqueKey, JsonSerializer.Serialize(keys, JsonOptions));
        }

        if (command.OpMode == "prefix_kvs")
        {
            var pairs = await database.ListKvsByPrefixAsync(command.Key, command.Limit, ct);
            var dto = pairs.Select(pair => new { key = pair.Key, value = pair.Value }).ToArray();
            return DbResponse.Success(command.UniqueKey, JsonSerializer.Serialize(dto, JsonOptions));
        }

        throw new InvalidOperationException($"unsupported op_mode: {command.OpMode}");
    }

    private static DbResponse? Validate(DbCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.UniqueKey))
        {
            command.UniqueKey = "";
        }

        if (string.IsNullOrWhiteSpace(command.DbName))
        {
            return DbResponse.Error(-101, "missing required field: db_name", command.UniqueKey);
        }

        if (string.IsNullOrWhiteSpace(command.Operation))
        {
            return DbResponse.Error(-101, "missing required field: operation", command.UniqueKey);
        }

        if (string.IsNullOrWhiteSpace(command.OpMode))
        {
            return DbResponse.Error(-101, "missing required field: op_mode", command.UniqueKey);
        }

        if (command.Operation is not ("put" or "get" or "delete" or "list"))
        {
            return DbResponse.Error(-301, $"unsupported operation: {command.Operation}", command.UniqueKey);
        }

        if ((command.Operation != "put" || command.OpMode != "kv") &&
            !command.IsBatch &&
            string.IsNullOrWhiteSpace(command.Key))
        {
            return DbResponse.Error(-101, "missing required field: key", command.UniqueKey);
        }

        return null;
    }

    private static IReadOnlyList<KeyValuePair<string, string>> ParseBatchValues(string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return [];
        }

        using var document = JsonDocument.Parse(rawValue);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("batch value must be an array");
        }

        var pairs = new List<KeyValuePair<string, string>>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("key", out var keyElement) ||
                !item.TryGetProperty("value", out var valueElement))
            {
                throw new JsonException("batch item must contain key and value");
            }

            var key = keyElement.ValueKind == JsonValueKind.String ? keyElement.GetString() ?? "" : keyElement.GetRawText();
            var value = valueElement.ValueKind == JsonValueKind.String ? valueElement.GetString() ?? "" : valueElement.GetRawText();
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new JsonException("batch item key is empty");
            }

            pairs.Add(new KeyValuePair<string, string>(key, value));
        }

        return pairs;
    }

    private static string AppendValue(string? existing, string newValue)
    {
        var values = new List<string>();
        if (!string.IsNullOrWhiteSpace(existing))
        {
            try
            {
                using var document = JsonDocument.Parse(existing);
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    values.AddRange(document.RootElement.EnumerateArray().Select(item =>
                        item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : item.GetRawText()));
                }
                else
                {
                    values.Add(existing);
                }
            }
            catch (JsonException)
            {
                values.Add(existing);
            }
        }

        values.Add(newValue);
        return JsonSerializer.Serialize(values, JsonOptions);
    }
}
