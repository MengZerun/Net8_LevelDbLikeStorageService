# C# .NET Framework 4.6.2 Client

示例 `Program.cs` 使用 `HttpWebRequest`，避免依赖 .NET 8 API。实际业务中可继续使用现有 `ComOther.GetLevelDbOpCmd` 生成 JSON 字符串，然后 POST 到 `http://127.0.0.1:9877/`。

如果要用 `JavaScriptSerializer`，项目需要引用 `System.Web.Extensions`。
