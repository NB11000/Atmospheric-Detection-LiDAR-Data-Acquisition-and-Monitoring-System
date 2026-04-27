# MQTT RPC 集成计划：作为 BackgroundService 叠加到现有 Web API

## 1. 核心原则

**零删除、零破坏**。现有 Web API 的一切（控制器、SignalR、Swagger、CORS、WebSocket、gRPC）保持原样不动。MQTT RPC 作为 **附加的通信通道**，通过 ASP.NET Core 原生的 `BackgroundService` 机制并行运行，与 HTTP 通道共享同一套服务层。

## 2. 现状分析

当前 WebAPI 项目 (`Microsoft.NET.Sdk.Web`) 包含 4 个控制器共 **26 个 API 端点**，所有业务逻辑最终委托给以下内部服务：

```
控制器 (HTTP)          内部服务 (业务逻辑)
─────────────────────  ─────────────────────────
ClientController    →  GrpcServiceImpl (gRPC 转发给子进程)
                   →  ConfigHelper (配置读写)
                   →  SystemStateService (状态缓存)
LaserController     →  CniLaser (串口激光控制)
                   →  ConfigHelper / SystemStateService
SystemStateController → SystemStateService (状态快照)
LogController       →  InMemorySink (Serilog 内存日志)
```

MQTT RPC Handler 将 **直接调用同一批内部服务**，不走控制器层，避免代码重复。

---

## 3. 目标架构（叠加模式）

```
┌──────────────────────────────────────────────────────────┐
│              ASP.NET Core WebApplication                  │
│                                                          │
│  ┌──────────────────────┐  ┌───────────────────────────┐ │
│  │  HTTP 通道 (保持不变) │  │  MQTT 通道 (新增)         │ │
│  │                      │  │                           │ │
│  │  Swagger UI          │  │  MqttRpcBackgroundService  │ │
│  │  CORS                │  │    ├─ MqttClient 连接管理  │ │
│  │  Controllers (4个)   │  │    ├─ MqttRpcServer 调度   │ │
│  │  SignalR Hub         │  │    ├─ CollectorHandler     │ │
│  │  WebSocket           │  │    ├─ LaserHandler         │ │
│  │  gRPC Service        │  │    ├─ SystemHandler        │ │
│  │                      │  │    └─ LogHandler           │ │
│  └──────────┬───────────┘  └─────────────┬─────────────┘ │
│             │                            │               │
│             └──────────┬─────────────────┘               │
│                        ▼                                 │
│  ┌─────────────────────────────────────────────────────┐ │
│  │  共享服务层 (两个通道共用，DI 单例)                    │ │
│  │  GrpcServiceImpl · CniLaser · ConfigHelper           │ │
│  │  SystemStateService · SignalRHubPublisher            │ │
│  │  UISharedBuffer · InMemorySink                       │ │
│  └─────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────┘
         │ gRPC                             │ MQTT
         ▼                                  ▼
  ┌──────────────┐              ┌──────────────────┐
  │ ConsoleApp1  │              │  MQTT Broker      │
  │ (子进程)      │              │  (localhost:1883) │
  └──────────────┘              └────────┬─────────┘
                                         │ MQTT RPC
                               ┌─────────┴─────────┐
                               │  远程调用方         │
                               │  (UI / 调度系统)   │
                               └───────────────────┘
```

- **HTTP 和 MQTT 是平等通道**，各自独立运行，由 ASP.NET Core 统一管理生命周期
- **MQTT 通道通过 `BackgroundService` 托管**，启动/停止与 WebApplication 生命周期绑定
- **服务层 100% 共享**，无需重复实现任何业务逻辑

---

## 4. MQTT RPC 协议设计

### 4.1 库选择

使用 **MQTTnet** + **MQTTnet.Extensions.Rpc**（官方扩展包）。

```xml
<!-- 仅新增以下两个包，现有包全部保留 -->
<PackageReference Include="MQTTnet" Version="5.0.*" />
<PackageReference Include="MQTTnet.Extensions.Rpc" Version="5.0.*" />
```

MQTTnet.Extensions.Rpc 提供：
- `MqttRpcServer` — 注册方法处理器，自动管理请求/响应主题订阅
- `MqttRpcClient` — 远程调用方使用（不在本范围内）
- 内置关联 ID (CorrelationId) 匹配请求与响应
- 超时控制、取消令牌支持

### 4.2 主题命名约定

采用 `MqttRpcTopicStrategy` 自定义模板：

```
$rpc/daq-master/{methodName}           ← 请求主题
$rpc/daq-master/{methodName}/{correlationId}  ← 响应主题
```

- `$rpc` — MQTTnet.Extensions.Rpc 默认基础前缀
- `daq-master` — 进程固定 ClientId（从配置读取）
- `{methodName}` — RPC 方法名，按 `{domain}/{action}` 结构（如 `collector/open_device`）
- `{correlationId}` — 库自动生成的 UUID

### 4.3 26 个 RPC 方法映射（与 HTTP 端点一一对应）

| HTTP 端点 | → | MQTT RPC 方法名 | 请求参数 | 响应类型 |
|---|---|---|---|---|
| `GET /api/collector/command/status` | → | `collector/status` | 无 | 匿名对象 `{clientId, connected, timestamp}` |
| `POST /api/collector/command` | → | `collector/command/send` | `string` | `AdResponse` (JSON) |
| `POST /api/collector/command/async` | → | `collector/command/send_async` | `string` | `{ accepted: true }` |
| `POST /api/collector/command/open` | → | `collector/open_device` | 无 | `CommandResult` |
| `POST /api/collector/command/open-again` | → | `collector/open_device_again` | 无 | `CommandResult` |
| `POST /api/collector/command/close` | → | `collector/close_device` | 无 | `CommandResult` |
| `POST /api/collector/command/start` | → | `collector/start_ad` | 无 | `CommandResult` |
| `POST /api/collector/command/stop` | → | `collector/stop_ad` | 无 | `CommandResult` |
| `POST /api/collector/command/ping` | → | `collector/ping` | 无 | `AdResponse` (JSON) |
| `POST /api/collector/command/exit` | → | `collector/exit` | 无 | `AdResponse` (JSON) |
| `POST /api/collector/command/config/read` | → | `collector/config/read` | 无 | `CaptureCardConfig` |
| `POST /api/collector/command/config/update` | → | `collector/config/update` | `CaptureCardConfig` | `CaptureCardConfig` |
| `GET /api/collector/command/config/default` | → | `collector/config/default` | 无 | `CaptureCardConfig` |
| `POST /api/laser/connect` | → | `laser/connect` | 无 | `CommandResult` |
| `POST /api/laser/disconnect` | → | `laser/disconnect` | 无 | `CommandResult` |
| `POST /api/laser/on` | → | `laser/on` | 无 | `CommandResult` |
| `POST /api/laser/off` | → | `laser/off` | 无 | `CommandResult` |
| `GET /api/laser/status` | → | `laser/status` | 无 | `{ connected, emissionOn, portName, timestamp }` |
| `POST /api/laser/config/update` | → | `laser/config/update` | `RadarConfig` | `RadarConfig` |
| `POST /api/laser/config/read` | → | `laser/config/read` | 无 | `RadarConfig` |
| `GET /api/system/state` | → | `system/state` | 无 | `SystemStateDto` |
| `GET /api/logs` | → | `logs/query` | `LogQueryParams` | `LogQueryResult` |
| `GET /api/logs/{level}` | → | `logs/by_level` | `LogByLevelParams` | `LogByLevelResult` |
| `GET /api/logs/levels` | → | `logs/level_stats` | 无 | `LogStatsResult` |
| `DELETE /api/logs` | → | `logs/clear` | 无 | `{ message }` |
| `GET /api/logs/health` | → | `logs/health` | 无 | `{ status, message }` |

### 4.4 序列化方案

复用 `System.Text.Json` + **源生成器**（与 `ConfigJsonContext` 同体系）：

- 请求/响应体：UTF8 JSON 序列化为 `byte[]`
- 新增 DTO 注册到 `ConfigJsonContext` 或独立 `MqttRpcJsonContext`
- Handler 内部强类型反序列化后调用服务层，结果序列化返回

### 4.5 错误处理

沿用 `CommandResult.Success/Code/Message` 模式，所有响应通过 JSON 体表达语义，MQTT 层不映射 HTTP 状态码。超时由 `MqttRpcServer` 内置机制（默认 60 秒，可配置）。

---

## 5. 实施步骤

### 步骤 1：添加 NuGet 包

**文件：`WebAPI.csproj`**

**动机**：添加 MQTTnet 运行时依赖，现有包全部保留不动。

```xml
<!-- 新增以下两行到 <ItemGroup> -->
<PackageReference Include="MQTTnet" Version="5.0.*" />
<PackageReference Include="MQTTnet.Extensions.Rpc" Version="5.0.*" />
```

> SDK 保持 `Microsoft.NET.Sdk.Web`，所有现有包（Swagger、gRPC、Serilog 等）不动。

### 步骤 2：添加 MQTT 配置选项类

**新建文件：`Models/MqttSettings.cs`**

**动机**：用 ASP.NET Core Options 模式管理 MQTT 连接参数，与现有 `CaptureCardConfig`/`RadarConfig` 风格一致。

```csharp
public class MqttSettings
{
    public string BrokerHost { get; set; } = "localhost";
    public int BrokerPort { get; set; } = 1883;
    public string ClientId { get; set; } = "daq-master";
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public int RpcTimeoutSeconds { get; set; } = 60;
    public int ReconnectDelaySeconds { get; set; } = 5;
}
```

### 步骤 3：添加 MQTT 配置到 appsettings.json

**动机**：将 MQTT 参数外置化，支持不同部署环境。

在现有 `appsettings.json` 中添加：

```json
{
  "Mqtt": {
    "BrokerHost": "localhost",
    "BrokerPort": 1883,
    "ClientId": "daq-master",
    "Username": "",
    "Password": "",
    "RpcTimeoutSeconds": 60,
    "ReconnectDelaySeconds": 5
  }
}
```

### 步骤 4：创建 MQTT RPC Handler 类

**新建目录：`MqttRpc/`**，包含 4 个文件：

**动机**：每个 Handler 负责一个领域（collector/laser/system/logs），直接调用服务层（与控制器的依赖注入完全一致），零代码重复。

| 文件 | 对应原控制器 | 方法数 | 注入的依赖 |
|---|---|---|---|
| `MqttRpc/CollectorHandler.cs` | `ClientController` | 13 | `GrpcServiceImpl`, `ConfigHelper`, `SystemStateService`, `ILogger<T>` |
| `MqttRpc/LaserHandler.cs` | `LaserController` | 7 | `CniLaser`, `ConfigHelper`, `SystemStateService`, `ILogger<T>` |
| `MqttRpc/SystemHandler.cs` | `SystemStateController` | 1 | `SystemStateService`, `ILogger<T>` |
| `MqttRpc/LogHandler.cs` | `LogController` | 5 | `ILogger<T>` + `InMemorySink.Instance` |

**Handler 通用结构**（示例）：

```csharp
public class CollectorHandler
{
    private readonly GrpcServiceImpl _grpc;
    private readonly ConfigHelper _config;
    private readonly SystemStateService _state;
    private readonly ILogger<CollectorHandler> _logger;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false,
        TypeInfoResolver = ConfigJsonContext.Default // 复用源生成器
    };

    public CollectorHandler(GrpcServiceImpl grpc, ConfigHelper config,
        SystemStateService state, ILogger<CollectorHandler> logger)
    {
        _grpc = grpc;
        _config = config;
        _state = state;
        _logger = logger;
    }

    public void RegisterAll(MqttRpcServer rpcServer)
    {
        rpcServer.RegisterHandler("collector/open_device", HandleOpenDevice);
        rpcServer.RegisterHandler("collector/start_ad", HandleStartAd);
        // ... 共 13 个注册
    }

    private async Task<byte[]> HandleOpenDevice(MqttRpcReceivedPacket request)
    {
        // 直接调用 GrpcServiceImpl，与 ClientController.OpenDevice() 逻辑完全一致
        var response = await _grpc.SendCommandToClientAndWaitResponse("数据采集子进程", "OPEN_DEVICE");
        var state = _state.Get_System_State_Struct();
        var result = new CommandResult
        {
            Success = state.Collector.DeviceOpened,
            Code = state.Collector.DeviceOpened ? "COLLECTOR_OPENED" : "COLLECTOR_OPEN_FAILED",
            Message = response.Content,
            State = null
        };
        return JsonSerializer.SerializeToUtf8Bytes(result, _jsonOptions);
    }
}
```

**关键设计**：Handler 调用 `GrpcServiceImpl` / `CniLaser` / `SystemStateService` 的方式与控制器**完全一致**——因为它们注入的是同一批 DI 单例。

### 步骤 5：创建 MQTT BackgroundService

**新建文件：`Service/MqttRpcBackgroundService.cs`**

**动机**：利用 ASP.NET Core `BackgroundService` 托管 MQTT 客户端生命周期，Framework 负责启动/停止/取消，无需手动管理。

```csharp
public class MqttRpcBackgroundService : BackgroundService
{
    private readonly ILogger<MqttRpcBackgroundService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptionsMonitor<MqttSettings> _mqttSettings;
    private IMqttClient _mqttClient;
    private MqttRpcServer _rpcServer;

    public MqttRpcBackgroundService(
        ILogger<MqttRpcBackgroundService> logger,
        IServiceProvider serviceProvider,
        IOptionsMonitor<MqttSettings> mqttSettings)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _mqttSettings = mqttSettings;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAndServeAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MQTT RPC 服务异常，{Delay}秒后重连",
                    _mqttSettings.CurrentValue.ReconnectDelaySeconds);
                await Task.Delay(
                    TimeSpan.FromSeconds(_mqttSettings.CurrentValue.ReconnectDelaySeconds),
                    stoppingToken);
            }
        }
    }

    private async Task ConnectAndServeAsync(CancellationToken cancellationToken)
    {
        var settings = _mqttSettings.CurrentValue;
        var factory = new MqttClientFactory();
        _mqttClient = factory.CreateMqttClient();

        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(settings.BrokerHost, settings.BrokerPort)
            .WithClientId(settings.ClientId)
            .Build();

        await _mqttClient.ConnectAsync(options, cancellationToken);
        _logger.LogInformation("已连接到 MQTT Broker {Host}:{Port}",
            settings.BrokerHost, settings.BrokerPort);

        _rpcServer = new MqttRpcServer(_mqttClient, new MqttRpcServerOptions
        {
            TopicGenerationStrategy = new MqttRpcTopicStrategy
            {
                RequestTopicTemplate = "$rpc/{clientId}/{methodName}",
                ResponseTopicTemplate = "$rpc/{clientId}/{methodName}/{correlationId}"
            }
        });

        // 从 DI 容器获取 Handler 并注册
        var collectorHandler = _serviceProvider.GetRequiredService<CollectorHandler>();
        var laserHandler = _serviceProvider.GetRequiredService<LaserHandler>();
        var systemHandler = _serviceProvider.GetRequiredService<SystemHandler>();
        var logHandler = _serviceProvider.GetRequiredService<LogHandler>();
        collectorHandler.RegisterAll(_rpcServer);
        laserHandler.RegisterAll(_rpcServer);
        systemHandler.RegisterAll(_rpcServer);
        logHandler.RegisterAll(_rpcServer);

        await _rpcServer.StartAsync(cancellationToken);
        _logger.LogInformation("MQTT RPC 服务端已启动，共注册 26 个方法");
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _rpcServer?.Dispose();
        if (_mqttClient?.IsConnected == true)
            await _mqttClient.DisconnectAsync(cancellationToken);
        _mqttClient?.Dispose();
        await base.StopAsync(cancellationToken);
    }
}
```

### 步骤 6：在 Program.cs 中注册服务

**文件：`Program.cs`**

**动机**：将新增的 BackgroundService、Handler、Options 注册到 DI 容器。现有代码全部保留，仅增加以下行。

在 `builder.Services.AddGrpc()` 区域附近新增：

```csharp
// ===== 新增：MQTT RPC 服务注册 =====
// 绑定 MQTT 配置选项
builder.Services.Configure<MqttSettings>(builder.Configuration.GetSection("Mqtt"));

// 注册 4 个 Handler（单例，通过 DI 注入共享服务）
builder.Services.AddSingleton<CollectorHandler>();
builder.Services.AddSingleton<LaserHandler>();
builder.Services.AddSingleton<SystemHandler>();
builder.Services.AddSingleton<LogHandler>();

// 将 MQTT RPC 作为 ASP.NET Core BackgroundService 托管
builder.Services.AddHostedService<MqttRpcBackgroundService>();
```

### 步骤 7：新增 MQTT RPC 请求参数的 DTO 定义

**新建文件：`Models/MqttRpcParams.cs`**

**动机**：为有参数的 RPC 方法（日志查询、配置更新等）定义类型安全的请求 DTO。

```csharp
// 日志查询参数（对应 GET /api/logs 的 query string）
public class LogQueryParams
{
    public int Limit { get; set; } = 100;
    public int Offset { get; set; } = 0;
    public string? Level { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

// 按级别查询参数（对应 GET /api/logs/{level}）
public class LogByLevelParams
{
    public string Level { get; set; } = string.Empty;
    public int Limit { get; set; } = 100;
}

// 通用命令发送参数（对应 POST /api/collector/command）
public class CommandSendParams
{
    public string Command { get; set; } = string.Empty;
}
```

### 步骤 8：在 GrpcServiceImpl 中保留 SignalR 依赖

**动机**：现有 `GrpcServiceImpl` 已通过 `SignalRHubPublisher` 向 Web 前端推送事件。叠加 MQTT 后，**SignalR 通道完全保留**，`GrpcServiceImpl.cs` **不做任何修改**。

> 之前的计划要求移除 SignalR 依赖——本计划明确**不做此修改**。SignalR 继续服务 Web 前端，MQTT RPC 服务远程调用方，两者并行不悖。

### 步骤 9：在 SystemStateService 中保留现有 using

**文件：`Service/SystemStateService.cs`**

不做修改。现有的 `using Microsoft.AspNetCore.SignalR` 和 `using WebAPI.Hubs` 保留（它们未被实际使用，仅为预留引用，不影响编译）。

---

## 6. 文件变更清单

| 操作 | 文件 | 说明 |
|---|---|---|
| 修改 | `WebAPI.csproj` | 新增 2 个 PackageReference |
| 修改 | `appsettings.json` | 新增 `Mqtt` 配置节点 |
| 修改 | `Program.cs` | 新增 MQTT 相关服务注册（约 10 行） |
| 新建 | `Models/MqttSettings.cs` | MQTT 配置选项类 |
| 新建 | `Models/MqttRpcParams.cs` | RPC 请求参数 DTO |
| 新建 | `MqttRpc/CollectorHandler.cs` | 采集卡领域 Handler (13 方法) |
| 新建 | `MqttRpc/LaserHandler.cs` | 激光器领域 Handler (7 方法) |
| 新建 | `MqttRpc/SystemHandler.cs` | 系统状态 Handler (1 方法) |
| 新建 | `MqttRpc/LogHandler.cs` | 日志查询 Handler (5 方法) |
| 新建 | `Service/MqttRpcBackgroundService.cs` | BackgroundService 托管类 |
| 不变 | `Controllers/` (4 个文件) | 完全保留 |
| 不变 | `Hubs/SystemStateHub.cs` | 完全保留 |
| 不变 | `Service/GrpcServiceImpl.cs` | 完全保留 |
| 不变 | `Service/SignalRHubPublisher.cs` | 完全保留 |
| 不变 | `Service/SystemStateService.cs` | 完全保留 |
| 不变 | `Service/CniLaser.cs` | 完全保留 |
| 不变 | `Tools/ConfigHelper.cs` | 完全保留 |
| 不变 | `Tools/Tool.cs` | 完全保留（含 WebSocket 方法） |

---

## 7. 额外考虑

### 7.1 WebSocket 波形数据流

原 33ms 高频 WebSocket 数据流不适合 MQTT RPC（请求-响应开销大）。如需通过 MQTT 传输波形数据，应在 `MqttRpcBackgroundService` 中额外注册一个 **MQTT 发布循环**，定时从 `UISharedBuffer` 读取帧并发布到主题 `daq/master/waveform`（发布-订阅模式，非 RPC）。

### 7.2 MQTT 状态变更推送

当前 `SignalRHubPublisher` 已通过 SignalR 向 Web 前端推送状态变更事件。如需 MQTT 侧也接收状态推送，可在 `GrpcServiceImpl` 收到事件时（或 `SystemStateService` 状态变更时）额外调用 `MqttRpcBackgroundService` 发布消息到主题 `daq/master/events/{type}`。

### 7.3 Broker 部署

- 推荐 **EMQX**（开源，WebSocket 原生支持）或 **Mosquitto**（轻量，适合嵌入式）
- 开发环境可直接部署在 localhost:1883

---

## 8. 待确认问题

1. **MQTT Broker 地址**：`localhost:1883` 还是远程服务器？
2. **认证**：Broker 是否需要用户名/密码？
3. **远程调用方**：MqttRpcClient 在哪个项目实现？是 Avalonia UI 还是独立的调度系统？
4. **MQTTnet 版本**：使用 5.0.x（最新稳定版）还是 4.x（LTS）？
5. **日志查询数据量**：是否需要为 MQTT 通道设置更严格的 `Limit` 默认值（如 50 而非 100），避免大量 JSON 阻塞 MQTT？

---

以上为完整计划。确认后我将按步骤实施。
