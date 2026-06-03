using StorageService.Api.Services;

namespace StorageService.Tests;

public sealed class DbCommandParserTests
{
    private readonly DbCommandParser _parser = new();

    [Fact]
    public void ParseSupportsStandardFieldsAndStringBatchFlag()
    {
        var result = _parser.Parse("""
        {
          "db_name": "tray",
          "is_batch": "false",
          "key": "k1",
          "key_list": "",
          "op_mode": "all_ow",
          "operation": "put",
          "uniqueKey": "u1",
          "value": {"a":1}
        }
        """);

        Assert.True(result.IsSuccess);
        Assert.Equal("tray", result.Command!.DbName);
        Assert.Equal("put", result.Command.Operation);
        Assert.Equal("all_ow", result.Command.OpMode);
        Assert.False(result.Command.IsBatch);
        Assert.Equal("{\"a\":1}", result.Command.Value);
    }

    [Fact]
    public void ParseSupportsOldFieldNamesAndBooleanBatchFlag()
    {
        var result = _parser.Parse("""
        {
          "key": "k1",
          "op": "put",
          "dbName": "tray",
          "opMode": "all_ow",
          "value": [1,2],
          "uniqueKey": "u1",
          "is_batch": true
        }
        """);

        Assert.True(result.IsSuccess);
        Assert.Equal("tray", result.Command!.DbName);
        Assert.Equal("put", result.Command.Operation);
        Assert.Equal("all_ow", result.Command.OpMode);
        Assert.True(result.Command.IsBatch);
        Assert.Equal("[1,2]", result.Command.Value);
    }

    [Fact]
    public void InvalidJsonReturnsCompatibilityError()
    {
        var result = _parser.Parse("{");

        Assert.False(result.IsSuccess);
        Assert.Equal(-100, result.Error!.ResultCode);
        Assert.Equal("invalid json", result.Error.Msg);
    }
}
