using System.Net.Http.Json;
using System.Text.Json;

using var client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:9877") };

var putRequest = new
{
    db_name = "tray",
    is_batch = "false",
    key = "T20260603001-002",
    key_list = "",
    op_mode = "all_ow",
    operation = "put",
    uniqueKey = $"net8-put-{DateTime.Now:yyyyMMddHHmmssfff}",
    value = JsonSerializer.Serialize(new { trayCode = "T20260603001", pos = 1, finalRes = "OK" })
};

var putResponse = await client.PostAsJsonAsync("/", putRequest);
Console.WriteLine(await putResponse.Content.ReadAsStringAsync());

var getRequest = new
{
    db_name = "tray",
    is_batch = "false",
    key = "T20260603001-002",
    key_list = "",
    op_mode = "all",
    operation = "get",
    uniqueKey = $"net8-get-{DateTime.Now:yyyyMMddHHmmssfff}",
    value = ""
};

var getResponse = await client.PostAsJsonAsync("/", getRequest);
Console.WriteLine(await getResponse.Content.ReadAsStringAsync());
Console.ReadKey();
