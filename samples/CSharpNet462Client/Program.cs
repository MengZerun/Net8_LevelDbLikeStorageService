using System;
using System.IO;
using System.Net;
using System.Text;

internal static class Program
{
    private static void Main()
    {
        var json = "{"
            + "\"db_name\":\"tray\","
            + "\"is_batch\":\"false\","
            + "\"key\":\"T20260603001-001\","
            + "\"key_list\":\"\","
            + "\"op_mode\":\"all_ow\","
            + "\"operation\":\"put\","
            + "\"uniqueKey\":\"net462-put-001\","
            + "\"value\":\"{\\\"trayCode\\\":\\\"T20260603001\\\",\\\"finalRes\\\":\\\"OK\\\"}\""
            + "}";

        Console.WriteLine(PostJson("http://127.0.0.1:9877/", json));
    }

    private static string PostJson(string url, string json)
    {
        var request = (HttpWebRequest)WebRequest.Create(url);
        request.Method = "POST";
        request.ContentType = "application/json";
        var bytes = Encoding.UTF8.GetBytes(json);
        request.ContentLength = bytes.Length;

        using (var stream = request.GetRequestStream())
        {
            stream.Write(bytes, 0, bytes.Length);
        }

        using (var response = (HttpWebResponse)request.GetResponse())
        using (var stream = response.GetResponseStream())
        using (var reader = new StreamReader(stream, Encoding.UTF8))
        {
            return reader.ReadToEnd();
        }
    }
}
