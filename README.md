# Net8_LevelDbLikeStorageService

这是一个用 C# / .NET 8 实现的 LevelDB 风格 HTTP 数据库服务，目标是兼容现有 `storage_0729` 服务的主要外部行为：HTTP POST 入口、`GetLevelDbOpCmd` JSON 字段、返回 JSON 结构、`config/` 目录、逻辑库名映射、Windows 控制台运行和 Windows Service 部署。

当前默认使用 `leveldb` 原生绑定引擎，NuGet 包为 `LevelDB.Net_All`，数据以 LevelDB 原生格式保存到每个库目录。项目仍保留内置 `file` KV 引擎作为开发/排障 fallback，切换引擎不影响 HTTP 协议。

## 兼容范围

- HTTP POST：`/`、`/db`、`/api/db`、`/api/storage`
- 运维接口：`GET /health`、`GET /api/dbs`
- 请求字段：`db_name`、`operation`、`op_mode`、`key`、`value`、`uniqueKey`、`key_list`、`is_batch`
- 旧字段兼容：`dbName -> db_name`、`op -> operation`、`opMode -> op_mode`
- 操作：`put`、`get`、`delete`、`list`
- 模式：`all_ow`、`all`、`last`、`prefix_keys`、`prefix_kvs`、`ap`、`kv`
- 批量写入：`put + kv + is_batch=true`
- 返回字段：`resultCode`、`msg`、`value`、`uniqueKey`

暂未实现：ZeroMQ/NetMQ、备份、加密、数据修复接口、RocksDB 引擎。`server_config.json` 会读取并记录日志，不会导致启动失败。

## 环境要求

- .NET 8 Runtime
- .NET SDK 8 或更高版本用于编译
- Windows Service 部署时需要管理员 PowerShell
- 默认 `leveldb` 引擎需要发布目录包含 `runtimes/win-x64/native/leveldb.dll`，NuGet 会随构建/发布自动复制

## 编译与测试

```powershell
dotnet restore .\Net8_LevelDbLikeStorageService.sln
dotnet build .\Net8_LevelDbLikeStorageService.sln
dotnet test .\Net8_LevelDbLikeStorageService.sln
```

## 运行

```powershell
dotnet run --project .\src\StorageService.Api -- -config config.json
```

默认监听：

```text
http://0.0.0.0:9877
```

也可以显式指定：

```powershell
dotnet run --project .\src\StorageService.Api -- -config config/config.json --urls http://0.0.0.0:9877 --console
```

健康检查：

```powershell
Invoke-RestMethod http://127.0.0.1:9877/health
```

## 发布

```powershell
.\scripts\publish-win-x64.ps1
```

等价命令：

```powershell
dotnet publish .\src\StorageService.Api\StorageService.Api.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=false `
  -o .\publish\win-x64
```

发布目录包含 `StorageService.Api.exe`、`appsettings.json`、`log4net.config`、`config/`、`README.md`，以及和 exe 同级的 `install-service.ps1`、`uninstall-service.ps1`、`restart-service.ps1`。发布脚本会同时创建和 exe 同级的 `log/` 目录。

## Windows Service

编译和发布都会把服务脚本复制到程序运行目录，和 `StorageService.Api.exe` 同级。Debug 编译后的目录示例：

```text
src/StorageService.Api/bin/Debug/net8.0/
  StorageService.Api.exe
  install-service.ps1
  uninstall-service.ps1
  restart-service.ps1
```

```powershell
cd .\publish\win-x64
.\install-service.ps1
.\restart-service.ps1
.\uninstall-service.ps1
```

安装脚本默认按自身所在目录定位 `StorageService.Api.exe` 和 `config/config.json`，因此脚本需要和数据库软件 exe 放在同一级目录。服务安装为 `Automatic`，开机会自动启动。安装脚本会创建防火墙规则，放行 `9877`、`9200`、`9201`、`9202`、`9203`。

## 配置文件

运行目录下需要有：

```text
config/
  config.json
  db.json
  db_config.json
  server_config.json
  log_config.json
  get_all_file.json
```

`db_config.json` 关键字段：

```json
{
  "root_path": "D:/dlib",
  "http_port": 9877,
  "diskCheckSpace": 1.0,
  "engine": "leveldb",
  "enableAdminApi": false,
  "maxRequestBodyMb": 100
}
```

`db.json` 负责逻辑库映射：

```json
{
  "tray": {
    "path": "D:/dlib/tray",
    "status": "active",
    "version": "0.0.0.1"
  }
}
```

开发测试环境可以把路径改成：

```text
D:/test/<db_name>
```

`engine` 可选值：

- `leveldb`：默认生产引擎，使用 `LevelDB.Net_All` 原生绑定。
- `file`：开发 fallback，引擎会在库目录生成 `data.json`。
- `rocksdb`：当前作为兼容配置读取，第一阶段会回退到 `file`。

## CRUD 示例

写入：

```powershell
$body = @{
  db_name = "tray"
  is_batch = "false"
  key = "T001-001"
  key_list = ""
  op_mode = "all_ow"
  operation = "put"
  uniqueKey = "u1"
  value = '{"trayCode":"T001","finalRes":"OK"}'
} | ConvertTo-Json -Compress
Invoke-RestMethod http://127.0.0.1:9877/ -Method Post -ContentType "application/json" -Body $body
```

读取：

```powershell
$body = @{
  db_name = "tray"
  key = "T001-001"
  key_list = ""
  op_mode = "all"
  operation = "get"
  uniqueKey = "u2"
  value = ""
} | ConvertTo-Json -Compress
Invoke-RestMethod http://127.0.0.1:9877/ -Method Post -ContentType "application/json" -Body $body
```

前缀 KV：

```json
{
  "db_name": "tray",
  "key": "T001",
  "operation": "list",
  "op_mode": "prefix_kvs",
  "value": "",
  "uniqueKey": "list001",
  "key_list": "",
  "is_batch": "false"
}
```

批量写入：

```json
{
  "db_name": "tray",
  "is_batch": "true",
  "key": "",
  "key_list": "",
  "op_mode": "kv",
  "operation": "put",
  "uniqueKey": "batch001",
  "value": [
    { "key": "k1", "value": "{\"a\":1}" },
    { "key": "k2", "value": "{\"a\":2}" }
  ]
}
```

`ap` 模式会把当前 `value` 追加到目标 key 对应的 JSON 数组；如果原值不是数组，会作为第一项字符串兼容保留。`last` 模式先查完整 key，找不到时按前缀查询并返回 ordinal 排序后的最后一条。

## 客户端示例

- .NET 8：`samples/CSharpNet8Client/Program.cs`
- .NET Framework 4.6.2：`samples/CSharpNet462Client/Program.cs`

现有业务可继续用 `ComOther.GetLevelDbOpCmd` 生成 JSON，请求 `value` 为字符串时服务会原样保存并以字符串返回。

## 日志

日志使用 `log4net`。默认目录来自 `config/log_config.json`。相对路径会按 `StorageService.Api.exe` 所在目录解析，因此默认日志在数据库软件同级目录下的 `log/`：

```text
log/Storage.log
log/Storage.error.log
```

每条请求记录 `uniqueKey`、`db_name`、`operation`、`op_mode`、`key`、请求体大小、耗时和返回码。

## 常见错误

- `-100 invalid json`：请求体不是合法 JSON。
- `-101 missing required field`：缺少必要字段。
- `-201 db_name not found`：逻辑库未配置或非 active。
- `-301 unsupported operation`：operation 不在 `put/get/delete/list` 内。
- `-302 unsupported op_mode`：当前 operation 不支持该模式。
- `-601 disk free space is lower than threshold`：写入前磁盘空间低于 `diskCheckSpace`。

## 数据目录

每个逻辑库一个目录。`leveldb` 引擎会创建 LevelDB 原生文件，例如：

```text
D:/dlib/tray/CURRENT
D:/dlib/tray/LOCK
D:/dlib/tray/LOG
D:/dlib/tray/MANIFEST-000001
```

`file` fallback 引擎会创建：

```text
D:/dlib/tray/data.json
```

该文件是紧凑 JSON 字典，便于开发验证与排障。后续接入 RocksDB 时只需实现新的 `IKvEngine`，HTTP 协议不变。
