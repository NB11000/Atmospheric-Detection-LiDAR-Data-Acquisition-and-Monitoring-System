# Master Controller Process — MQTT RPC 主通道架构迁移计划

---

## 1. 目标架构

### 1.1 现状 → 目标

```
【现状】                               【目标】
Avalonia UI ──HTTP──► WebAPI           Avalonia UI ──MQTT RPC──► Master Process
              ──SignalR──►                      ──MQTT Pub/Sub──► (事件推送)
              ──WebSocket►波形                   ──MQTT Pub/Sub──► (波形数据)
                                                 
WebAPI ──gRPC──► ConsoleApp1            Master Process ──gRPC──► ConsoleApp1 (不变)
```

**核心变更**：
1. WebAPI 项目转型为 **Master Controller Process**，MQTT RPC 成为**主通信通道**
2. 控制器/HTTP endpoints **保留**作为次级通道（调试、兼容、健康检查）
3. SignalR 事件推送 → MQTT 发布-订阅主题
4. WebSocket 波形流 → MQTT 发布-订阅（降低频率至 ~100ms）

### 1.2 进程内部架构

```
┌──────────────────────────────────────────────────────────┐
│              ASP.NET Core WebApplication                  │
│                                                          │
│  ┌──────────────────────┐  ┌───────────────────────────┐ │
│  │  HTTP 通道 (次级保留) │  │  MQTT 通道 (主通道)         │ │
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

---

## 2. MQTT RPC 协议设计

### 2.1 库选型

使用已安装的 `MQTTnet` 5.1.0 + `MQTTnet.Extensions.Rpc` 5.1.0（无需新增 NuGet 包）。

### 2.2 主题命名约定

采用自定义 `MqttRpcTopicStrategy`：

```
请求主题:  $rpc/{machineId}/{domain}/{action}
响应主题:  $rpc/{machineId}/{domain}/{action}/{correlationId}
```

示例：
```
$rpc/daq-srv-01/collector/open_device
$rpc/daq-srv-01/collector/open_device/a1b2c3d4
$rpc/daq-srv-01/laser/status
$rpc/daq-srv-01/system/state
$rpc/daq-srv-01/logs/query
```

- `$rpc` — MQTTnet.Extensions.Rpc 默认前缀
- `{machineId}` — 机器/进程标识（从配置读取，如 `daq-srv-01`），支持多机部署
- `{domain}/{action}` — 领域/操作两级结构
- `{correlationId}` — 库自动生成的 UUID，用于请求-响应匹配

### 2.3 事件推送主题（MQTT Pub/Sub，非 RPC）

```
daq/{machineId}/events/state_changed    — 状态变更事件
daq/{machineId}/events/device_alarm     — 设备报警事件
daq/{machineId}/events/data_updated     — 数据更新事件
daq/{machineId}/waveform/ch1            — 通道1波形数据
daq/{machineId}/waveform/ch2            — 通道2波形数据
```

### 2.4 26 个 RPC 方法映射

与 HTTP 端点一一对应，无遗漏：

| HTTP 端点 | MQTT RPC 方法名 | 请求参数 | 响应类型 |
|---|---|---|---|
| `GET /api/collector/command/status` | `collector/status` | 无 | `{clientId, connected, timestamp}` |
| `POST /api/collector/command` | `collector/command/send` | `string` | `AdResponse` JSON |
| `POST /api/collector/command/async` | `collector/command/send_async` | `string` | `{accepted: true}` |
| `POST /api/collector/command/open` | `collector/open_device` | 无 | `CommandResult` |
| `POST /api/collector/command/open-again` | `collector/open_device_again` | 无 | `CommandResult` |
| `POST /api/collector/command/close` | `collector/close_device` | 无 | `CommandResult` |
| `POST /api/collector/command/start` | `collector/start_ad` | 无 | `CommandResult` |
| `POST /api/collector/command/stop` | `collector/stop_ad` | 无 | `CommandResult` |
| `POST /api/collector/command/ping` | `collector/ping` | 无 | `AdResponse` JSON |
| `POST /api/collector/command/exit` | `collector/exit` | 无 | `AdResponse` JSON |
| `POST /api/collector/command/config/read` | `collector/config/read` | 无 | `CaptureCardConfig` |
| `POST /api/collector/command/config/update` | `collector/config/update` | `CaptureCardConfig` | `CaptureCardConfig` |
| `GET /api/collector/command/config/default` | `collector/config/default` | 无 | `CaptureCardConfig` |
| `POST /api/laser/connect` | `laser/connect` | 无 | `CommandResult` |
| `POST /api/laser/disconnect` | `laser/disconnect` | 无 | `CommandResult` |
| `POST /api/laser/on` | `laser/on` | 无 | `CommandResult` |
| `POST /api/laser/off` | `laser/off` | 无 | `CommandResult` |
| `GET /api/laser/status` | `laser/status` | 无 | `{connected, emissionOn, portName, timestamp}` |
| `POST /api/laser/config/update` | `laser/config/update` | `RadarConfig` | `RadarConfig` |
| `POST /api/laser/config/read` | `laser/config/read` | 无 | `RadarConfig` |
| `GET /api/system/state` | `system/state` | 无 | `SystemStateDto` |
| `GET /api/logs` | `logs/query` | `LogQueryParams` | `LogQueryResult` |
| `GET /api/logs/{level}` | `logs/by_level` | `LogByLevelParams` | `LogByLevelResult` |
| `GET /api/logs/levels` | `logs/level_stats` | 无 | `LogStatsResult` |
| `DELETE /api/logs` | `logs/clear` | 无 | `{message}` |
| `GET /api/logs/health` | `logs/health` | 无 | `{status, message}` |

### 2.5 序列化方案

- **序列化库**：`System.Text.Json`（复用现有项目依赖）
- **源生成器**：复用 `ConfigJsonContext` 或新增独立 `MqttRpcJsonContext`
- **数据格式**：请求/响应均为 UTF8 JSON `byte[]`
- **RPC 负载**：Handler 内部强类型反序列化 → 调用服务层 → 结果序列化返回
- **波形数据**：二进制 `byte[]` 直接发布，不经过 JSON（减少序列化开销）

### 2.6 错误处理策略

- 沿用现有 `CommandResult` 模式（`Success` / `Code` / `Message`），MQTT 层面不映射 HTTP 状态码
- 超时：由 `MqttRpcServer` 内置机制处理（默认 60 秒，可通过 `MqttSettings.RpcTimeoutSeconds` 配置）
- 连接断开：`BackgroundService` 自动重连（可配置重连间隔）
- 服务端异常：Handler 内部 try-catch，返回 `CommandResult` 格式的错误信息

---

## 3. SignalR → MQTT 事件推送迁移

### 3.1 当前 SignalR 事件

| 事件 | 触发源 | 触发时机 |
|---|---|---|
| `StateChanged` | `GrpcServiceImpl` / `CniLaser` | 子进程连接/断开、设备操作响应、错误上报、激光器状态变更 |
| `DeviceAlarm` | 暂未实际调用 | 预留设备报警 |
| `DataUpdated` | 暂未实际调用 | 预留数据更新 |

### 3.2 替换方案

新增 `MqttEventPublisher` 服务（单例），封装 MQTT 后台发布逻辑：

```csharp
public class MqttEventPublisher
{
    private readonly IMqttClient _mqttClient; // 由 BackgroundService 注入同一个 MQTT 连接
    
    public async Task PublishStateChangedAsync(string eventType, string source, string reason, string message);
    public async Task PublishDeviceAlarmAsync(string alarmType, string device, string message, int severity);
    public async Task PublishDataUpdatedAsync(string dataType, object data);
}
```

- `GrpcServiceImpl` 和 `CniLaser` 中原来调用 `SignalRHubPublisher` 的地方改为调用 `MqttEventPublisher`
- `SignalRHubPublisher` **保留但不作为主通道**——可同时发布到 MQTT 和 SignalR，确保过渡期兼容
- SignalR Hub 端点继续存在，不做删除

### 3.3 QOS 与保留消息

| 主题 | QOS | Retain | 说明 |
|---|---|---|---|
| `daq/{id}/events/state_changed` | 1 | false | 状态变更，即时事件 |
| `daq/{id}/events/device_alarm` | 1 | true | 设备报警，客户端重连后需获取最新报警 |
| `daq/{id}/events/data_updated` | 0 | false | 非关键数据，允许丢失 |
| `daq/{id}/waveform/ch1` | 0 | false | 高频波形，允许丢失 |
| `daq/{id}/waveform/ch2` | 0 | false | 高频波形，允许丢失 |

---

## 4. 波形数据 MQTT 发布方案

### 4.1 当前方案（待替换）

WebSocket `/ws/ui-data` 每 33ms 从 `UISharedBuffer` 读取 1000 点 × 2 通道的双精度数组，以二进制格式写入 WebSocket 帧。

### 4.2 MQTT 替代方案

新增 `MqttWaveformPublisher` 内部类（在 `MqttRpcBackgroundService` 中），定时从共享内存读取波形并发布：

- **频率**：100ms（10Hz），比原 33ms（30Hz）降低，适应 MQTT 吞吐
- **数据格式**：二进制 `byte[]`（双通道 1000 点 × 8 字节 = 8000 字节/通道），不经过 JSON
- **主题**：`daq/{machineId}/waveform/ch1` 和 `daq/{machineId}/waveform/ch2`
- **QOS**：0（允许丢失，保证低延迟）
- **生命周期**：与 BackgroundService 绑定，采集子进程连接后启动，断开后停止

### 4.3 WebSocket 端点

保留 `/ws/ui-data` 不做删除——作为次级通道，供有 WebSocket 能力的客户端直接使用。

---

## 5. 服务层解耦与重构

### 5.1 GrpcServiceImpl 变更

`GrpcServiceImpl.cs` 当前同时依赖 `SignalRHubPublisher`。修改为：

```
变更前：                                           变更后：
GrpcServiceImpl  ──► SignalRHubPublisher         GrpcServiceImpl  ──► MqttEventPublisher (主)
                                                  GrpcServiceImpl  ──► SignalRHubPublisher (保留，兼容)
```

具体修改（最小化变更）：
- 构造函数新增 `MqttEventPublisher` 参数
- 所有 `_hubPublisher.PublishStateChangedAsync()` 调用**之前**增加 `_mqttEventPublisher.PublishStateChangedAsync()` 调用
- 不删除 SignalR 调用，实现双通道并行推送

### 5.2 CniLaser 变更

同理，`CniLaser.cs` 当前依赖 `SignalRHubPublisher`：
- 构造函数新增 `MqttEventPublisher` 参数
- `PublishLaserStateChangedAsync()` 方法增加 MQTT 推送逻辑（保留 SignalR 调用）
- `UpdateLaserStateCache()` 保持不变（仅更新 SystemStateService 缓存）

### 5.3 SystemStateService 变更

**不修改**。`SystemStateService` 本身不依赖 SignalR，只负责状态缓存和快照查询。

### 5.4 ConfigHelper 变更

**不修改**。配置读写逻辑与通信通道无关。

---

## 6. 实施步骤

### 步骤 1：添加 MQTT 配置选项模型

**文件**：`Models/MqttSettings.cs`（新建）

**动机**：将 MQTT 连接参数从硬编码中解耦，支持 `appsettings.json` 配置和运行时修改。

```csharp
public class MqttSettings
{
    public string BrokerHost { get; set; } = "localhost";
    public int BrokerPort { get; set; } = 1883;
    public string MachineId { get; set; } = "daq-srv-01";      // 机器标识，用于主题前缀
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public int RpcTimeoutSeconds { get; set; } = 60;           // RPC 超时
    public int ReconnectDelaySeconds { get; set; } = 5;         // 断线重连间隔
    public int WaveformPublishIntervalMs { get; set; } = 100;   // 波形发布间隔
}
```

### 步骤 2：添加 appsettings.json MQTT 配置节点

**文件**：`appsettings.json`（修改，新增节点）

**动机**：外置化配置参数，不同部署环境使用不同 Broker 地址。

```json
{
  "Mqtt": {
    "BrokerHost": "localhost",
    "BrokerPort": 1883,
    "MachineId": "daq-srv-01",
    "Username": "",
    "Password": "",
    "RpcTimeoutSeconds": 60,
    "ReconnectDelaySeconds": 5,
    "WaveformPublishIntervalMs": 100
  }
}
```

### 步骤 3：创建 MQTT RPC 请求参数 DTO

**文件**：`Models/MqttRpcParams.cs`（新建）

**动机**：为有参数 RPC 方法提供类型安全的请求体定义。

```csharp
// 日志查询参数
public class LogQueryParams
{
    public int Limit { get; set; } = 100;
    public int Offset { get; set; } = 0;
    public string? Level { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

// 按级别查询参数
public class LogByLevelParams
{
    public string Level { get; set; } = string.Empty;
    public int Limit { get; set; } = 100;
}

// 通用命令发送参数
public class CommandSendParams
{
    public string Command { get; set; } = string.Empty;
}
```

### 步骤 4：创建 MQTT 事件发布服务

**文件**：`Service/MqttEventPublisher.cs`（新建）

**动机**：封装 MQTT 发布-订阅逻辑，替换 `SignalRHubPublisher` 作为主事件通道。`SignalRHubPublisher` 保留不变。

核心接口：

```csharp
public class MqttEventPublisher
{
    // 通过属性设置 MQTT 客户端（由 BackgroundService 注入，避免循环依赖）
    public IMqttClient MqttClient { get; set; }
    private readonly MqttSettings _settings;
    private readonly SystemStateService _stateService;
    private readonly ILogger<MqttEventPublisher> _logger;
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = false };

    /// 发布状态变更事件到 MQTT
    public async Task PublishStateChangedAsync(string eventType, string source, string reason, string message)
    {
        if (MqttClient?.IsConnected != true) return;
        var state = _stateService.GetSystemState();
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new MqttStateChangedEvent { EventType = eventType, Source = source, Reason = reason, Message = message, State = state, Timestamp = DateTime.Now },
            _jsonOptions);
        await MqttClient.PublishBinaryAsync($"daq/{_settings.MachineId}/events/state_changed", payload, MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce);
    }

    /// 发布设备报警事件
    public async Task PublishDeviceAlarmAsync(string alarmType, string device, string message, int severity)

    /// 发布数据更新事件
    public async Task PublishDataUpdatedAsync(string dataType, object data)
}
```

### 步骤 5：创建 4 个 MQTT RPC Handler

**目录**：`MqttRpc/`（新建）

**动机**：每个 Handler 对应一个领域，直接调用共享服务层（与控制器的依赖注入完全一致）。Handler 与控制器的关键区别：返回值是 `byte[]`，直接序列化为 JSON 返回给 MQTT 调用方。

#### 5a. `MqttRpc/CollectorHandler.cs`

- 13 个 RPC 方法注册
- 注入依赖：`GrpcServiceImpl`、`ConfigHelper`、`SystemStateService`、`ILogger<CollectorHandler>`
- 每个方法的逻辑与 `ClientController` 中对应方法完全一致（剥离 HTTP 层包装）
- `RegisterAll(MqttRpcServer)` 方法批量注册所有 RPC handler

#### 5b. `MqttRpc/LaserHandler.cs`

- 7 个 RPC 方法注册
- 注入依赖：`CniLaser`、`ConfigHelper`、`SystemStateService`、`ILogger<LaserHandler>`
- 方法委托签名：`Func<MqttRpcReceivedPacket, Task<byte[]>>`

#### 5c. `MqttRpc/SystemHandler.cs`

- 1 个 RPC 方法：`system/state`
- 注入依赖：`SystemStateService`、`ILogger<SystemHandler>`

#### 5d. `MqttRpc/LogHandler.cs`

- 5 个 RPC 方法：`logs/query`、`logs/by_level`、`logs/level_stats`、`logs/clear`、`logs/health`
- 注入依赖：`ILogger<LogHandler>` + `InMemorySink.Instance`（Serilog 全局单例）

### 步骤 6：创建 MQTT BackgroundService（核心）

**文件**：`Service/MqttRpcBackgroundService.cs`（新建）

**动机**：利用 ASP.NET Core `BackgroundService` 托管 MQTT 客户端的完整生命周期。

核心职责：
1. **连接管理**：连接/重连 MQTT Broker，使用指数退避策略
2. **RPC 服务端**：创建 `MqttRpcServer`，注册 4 个 Handler 的 26 个方法
3. **波形发布**：定时从 `UISharedBuffer` 读取双通道数据，二进制发布到 MQTT
4. **生命周期**：启动时连接 → 运行时服务 → 停止时优雅断开
5. **事件发布器绑定**：将 MQTT 客户端实例注入 `MqttEventPublisher`

```csharp
public class MqttRpcBackgroundService : BackgroundService
{
    // 注入
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptionsMonitor<MqttSettings> _mqttSettings;
    private readonly MqttEventPublisher _eventPublisher;
    private readonly ILogger<MqttRpcBackgroundService> _logger;
    
    // 运行时状态
    private IMqttClient _mqttClient;
    private MqttRpcServer _rpcServer;
    private Task _waveformPublishLoop;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAndServeAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "MQTT 连接异常，{Delay}s 后重连", _mqttSettings.CurrentValue.ReconnectDelaySeconds);
                await Task.Delay(TimeSpan.FromSeconds(_mqttSettings.CurrentValue.ReconnectDelaySeconds), stoppingToken);
            }
        }
    }

    private async Task ConnectAndServeAsync(CancellationToken ct)
    {
        var settings = _mqttSettings.CurrentValue;
        var factory = new MqttClientFactory();
        _mqttClient = factory.CreateMqttClient();
        
        // 将 MQTT 客户端注入事件发布器
        _eventPublisher.MqttClient = _mqttClient;
        
        // 连接 Broker
        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(settings.BrokerHost, settings.BrokerPort)
            .WithClientId(settings.MachineId)
            .Build();
        await _mqttClient.ConnectAsync(options, ct);
        
        // 创建 RPC 服务端
        _rpcServer = new MqttRpcServer(_mqttClient, new MqttRpcServerOptions
        {
            TopicGenerationStrategy = new MqttRpcTopicStrategy
            {
                RequestTopicTemplate = "$rpc/" + settings.MachineId + "/{methodName}",
                ResponseTopicTemplate = "$rpc/" + settings.MachineId + "/{methodName}/{correlationId}"
            }
        });
        
        // 注册 26 个 RPC 方法
        _serviceProvider.GetRequiredService<CollectorHandler>().RegisterAll(_rpcServer);
        _serviceProvider.GetRequiredService<LaserHandler>().RegisterAll(_rpcServer);
        _serviceProvider.GetRequiredService<SystemHandler>().RegisterAll(_rpcServer);
        _serviceProvider.GetRequiredService<LogHandler>().RegisterAll(_rpcServer);
        
        await _rpcServer.StartAsync(ct);
        
        // 启动波形发布循环
        _waveformPublishLoop = PublishWaveformLoopAsync(ct);
        
        // 阻塞直到取消
        await Task.Delay(Timeout.Infinite, ct);
    }

    private async Task PublishWaveformLoopAsync(CancellationToken ct)
    {
        var uiBuffer = _serviceProvider.GetRequiredService<UISharedBuffer>();
        var settings = _mqttSettings.CurrentValue;
        
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_mqttClient?.IsConnected == true)
                {
                    // 从共享内存读取双通道波形数据
                    var ch1Data = uiBuffer.ReadChannel1(); // byte[8000]
                    var ch2Data = uiBuffer.ReadChannel2();
                    
                    await Task.WhenAll(
                        _mqttClient.PublishBinaryAsync($"daq/{settings.MachineId}/waveform/ch1", ch1Data, MqttQualityOfServiceLevel.AtMostOnce),
                        _mqttClient.PublishBinaryAsync($"daq/{settings.MachineId}/waveform/ch2", ch2Data, MqttQualityOfServiceLevel.AtMostOnce)
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "波形数据发布失败");
            }
            
            await Task.Delay(settings.WaveformPublishIntervalMs, ct);
        }
    }

    public override async Task StopAsync(CancellationToken ct)
    {
        _rpcServer?.Dispose();
        if (_mqttClient?.IsConnected == true)
            await _mqttClient.DisconnectAsync(ct);
        _mqttClient?.Dispose();
        await base.StopAsync(ct);
    }
}
```

### 步骤 7：在 GrpcServiceImpl 中接入 MQTT 事件推送

**文件**：`Service/GrpcServiceImpl.cs`（修改）

**动机**：在现有 SignalR 推送基础上，增加 MQTT 事件推送，实现双通道并行。

**修改点**：
1. 构造函数新增 `MqttEventPublisher` 参数
2. 在 `Communicate()` 方法中，所有 `_hubPublisher.PublishStateChangedAsync()` 调用前增加 `_mqttEventPublisher.PublishStateChangedAsync()`
3. 在 `PublishCollectorDisconnectedAsync()` 中增加 MQTT 事件推送
4. **保留**所有现有 SignalR 推送调用，不做删除

### 步骤 8：在 CniLaser 中接入 MQTT 事件推送

**文件**：`Service/CniLaser.cs`（修改）

**修改点**：
1. 构造函数新增 `MqttEventPublisher` 参数（可选，配置为 null 时不启用）
2. `PublishLaserStateChangedAsync()` 方法增加 MQTT 推送（保留 SignalR 调用）

### 步骤 9：在 Program.cs 中注册新服务

**文件**：`Program.cs`（修改，约 20 行新增）

**动机**：将所有新服务注册到 DI 容器，使 `BackgroundService` 能够获取 Handler、发布器等。

在现有 DI 注册区域（`builder.Services.AddSignalR()` 之后）新增：

```csharp
// ===== MQTT RPC 主通道服务注册 =====
// MQTT 配置选项绑定
builder.Services.Configure<MqttSettings>(builder.Configuration.GetSection("Mqtt"));

// MQTT 事件发布器（单例，替代 SignalR 作为主事件通道）
builder.Services.AddSingleton<MqttEventPublisher>();

// 4 个 RPC Handler（单例，通过 DI 注入共享服务层）
builder.Services.AddSingleton<CollectorHandler>();
builder.Services.AddSingleton<LaserHandler>();
builder.Services.AddSingleton<SystemHandler>();
builder.Services.AddSingleton<LogHandler>();

// MQTT RPC BackgroundService（托管 MQTT 客户端生命周期）
builder.Services.AddHostedService<MqttRpcBackgroundService>();
```

### 步骤 10：更新 UISharedBuffer 以支持读取接口

**文件**：`Service/SharedMemoryServer.cs`（修改，视需要）

**动机**：`MqttWaveformPublisher` 需要从共享内存读取波形数据。如果 `UISharedBuffer` 已有公开的读取方法，则无需修改；否则增加 `ReadChannel1()` / `ReadChannel2()` 方法。

---

## 7. 文件变更清单

| 操作 | 文件路径 | 说明 |
|---|---|---|
| **新建** | `Models/MqttSettings.cs` | MQTT 配置选项类 |
| **新建** | `Models/MqttRpcParams.cs` | RPC 请求参数 DTO |
| **新建** | `Service/MqttEventPublisher.cs` | MQTT 事件发布服务（替换 SignalR 作为主通道） |
| **新建** | `MqttRpc/CollectorHandler.cs` | 采集卡领域 RPC Handler (13 方法) |
| **新建** | `MqttRpc/LaserHandler.cs` | 激光器领域 RPC Handler (7 方法) |
| **新建** | `MqttRpc/SystemHandler.cs` | 系统状态 RPC Handler (1 方法) |
| **新建** | `MqttRpc/LogHandler.cs` | 日志查询 RPC Handler (5 方法) |
| **新建** | `Service/MqttRpcBackgroundService.cs` | BackgroundService 托管类（连接、RPC 服务端、波形发布） |
| **修改** | `Program.cs` | 新增 MQTT 相关服务注册（~20 行） |
| **修改** | `appsettings.json` | 新增 `Mqtt` 配置节点 |
| **修改** | `Service/GrpcServiceImpl.cs` | 构造函数新增 MqttEventPublisher，增加 MQTT 事件推送调用 |
| **修改** | `Service/CniLaser.cs` | 构造函数新增 MqttEventPublisher，增加 MQTT 事件推送 |
| **可能修改** | `Service/SharedMemoryServer.cs` | 如需要，增加波形读取接口 |
| **保留不变** | `Controllers/` (4 个文件) | HTTP 次级通道完整保留 |
| **保留不变** | `Hubs/SystemStateHub.cs` | SignalR Hub 保留 |
| **保留不变** | `Service/SignalRHubPublisher.cs` | SignalR 发布器保留（兼容过渡） |
| **保留不变** | `Service/SystemStateService.cs` | 无需修改 |
| **保留不变** | `Tools/ConfigHelper.cs` | 无需修改 |
| **保留不变** | `WebAPI.csproj` | NuGet 包已满足，无需新增 |

---

## 8. 风险与注意事项

### 8.1 波形数据延迟

- 33ms → 100ms 频率降低 3 倍，UI 上波形刷新会有可感知的卡顿
- 缓解方案：`WaveformPublishIntervalMs` 配置项可调，方便根据实际 MQTT Broker 性能调优
- 当 MQTT Broker 与 Master Process 部署在同一机器时，延迟可控在 10-20ms

### 8.2 MQTT Broker 依赖

- Master Process 启动依赖 MQTT Broker 可用
- BackgroundService 内置重连机制，Broker 暂时不可用时不会崩溃
- 建议部署 EMQX 或 Mosquitto 在 localhost 或同一内网

### 8.3 MQTTnet.Extensions.Rpc API 兼容性

- 当前项目已安装 5.1.0.1559 版本
- `MqttRpcServer` / `MqttRpcTopicStrategy` / `MqttRpcServerOptions` API 需在实现时确认具体参数名和方法签名
- 如果 API 有差异，以实际安装版本的 IntelliSense 为准

### 8.4 双通道一致性问题

- HTTP 和 MQTT RPC 同时访问同一服务层（DI 单例）
- 两个通道可能并发调用 `GrpcServiceImpl.SendCommandToClientAndWaitResponse()`，导致状态竞争
- **缓解**：`GrpcServiceImpl` 内部已使用 `ConcurrentDictionary` + `TaskCompletionSource`，每条指令有独立 `requestId`，天然支持并发

### 8.5 MQTT 消息大小限制

- 日志查询可能返回大量数据（最多 1000 条）
- **缓解**：为 MQTT 通道设置更严格的默认 `Limit`（如 50），避免大 JSON 阻塞 MQTT 通信
- 客户端可通过 `limit` 参数自行控制

---

## 9. 客户端（调用方）实施指南

### 9.1 MQTT RPC 客户端示例（Avalonia UI 侧）

```csharp
// 使用 MqttRpcClient 调用远程方法
var rpcClient = new MqttRpcClient(mqttClient, new MqttRpcClientOptions
{
    TopicGenerationStrategy = new MqttRpcTopicStrategy
    {
        RequestTopicTemplate = "$rpc/{clientId}/{methodName}",
        ResponseTopicTemplate = "$rpc/{clientId}/{methodName}/{correlationId}"
    }
});

// 发起 RPC 调用
var requestBytes = Array.Empty<byte>(); // 无参数
var responseBytes = await rpcClient.ExecuteAsync(TimeSpan.FromSeconds(10), "collector/open_device", requestBytes);
var result = JsonSerializer.Deserialize<CommandResult>(responseBytes);
```

### 9.2 事件订阅示例

```csharp
// 订阅状态变更事件
await mqttClient.SubscribeAsync("daq/daq-srv-01/events/state_changed", MqttQualityOfServiceLevel.AtLeastOnce);
mqttClient.ApplicationMessageReceivedAsync += e =>
{
    if (e.ApplicationMessage.Topic == "daq/daq-srv-01/events/state_changed")
    {
        var eventData = JsonSerializer.Deserialize<MqttStateChangedEvent>(e.ApplicationMessage.PayloadSegment);
        // 更新 UI
    }
};

// 订阅波形数据
await mqttClient.SubscribeAsync("daq/daq-srv-01/waveform/ch1", MqttQualityOfServiceLevel.AtMostOnce);
```

---

## 10. 实施顺序（建议）

| 顺序 | 步骤 | 原因 |
|---|---|---|
| 1 | 步骤 1-2：配置模型 + appsettings.json | 基础设施先行，后续代码都依赖 MqttSettings |
| 2 | 步骤 3：请求参数 DTO | Handler 实现时需要这些类型 |
| 3 | 步骤 4：MqttEventPublisher | GrpcServiceImpl 和 CniLaser 修改时需要引用 |
| 4 | 步骤 5：4 个 RPC Handler | 核心业务逻辑迁移 |
| 5 | 步骤 6：MqttRpcBackgroundService | 连接管理与 Handler 注册 |
| 6 | 步骤 7-8：GrpcServiceImpl + CniLaser 接入 MQTT 事件 | 事件通道打通 |
| 7 | 步骤 9：Program.cs 注册 | 所有服务注册，确保 DI 链路完整 |
| 8 | 步骤 10：UISharedBuffer 读取接口 | 波形发布循环依赖 |

每一步完成后进行一次编译验证，确保没有破坏现有功能。

---

## 11. MqttRpcBackgroundService 全面优化方案

> 基于 MQTTnet 5.1.0 实际 API 审查后的重构方案。

### 11.1 问题清单

#### A. MQTTnet 原生能力未使用

| # | 行号 | 问题 | 说明 |
|---|------|------|------|
| A1 | 73-90 | 手动 `while` 重连循环 | MQTTnet 提供 `DisconnectedAsync` 事件，应在该事件中触发重连而非轮询循环 |
| A2 | 117-125 | 未配置 `KeepAlivePeriod` | 未设置心跳间隔，MQTTnet 无法自动发送 PINGREQ，连接空闲时可能被 Broker 断开 |
| A3 | 117-125 | 未配置 `WithCleanSession` | 不清除会话可能导致 Broker 积压离线消息 |
| A4 | 117-125 | 未配置 `WithTimeout` | 连接超时使用默认值（可能很长），未显式控制 |
| A5 | 117-125 | 未配置遗嘱消息 (`WillTopic/WillPayload`) | 进程崩溃时无法通知订阅者，缺少"进程死亡"事件推送 |
| A6 | 223 | 手动拼接 `topic + "/response"` | MQTT 5.0 原生支持 `MqttApplicationMessage.ResponseTopic`，可直接读取无需手动拼接 |
| A7 | 231-232 | `Encoding.UTF8.GetBytes()` 硬编码错误 JSON | 每次异常都新建字符串和字节数组，可预计算静态模板 |
| A8 | 264-268 | `MqttApplicationMessageBuilder` 每次新建 | 每次 RPC 响应均 new Builder → Build，可预构建消息模板 |
| A9 | 316-326 | 波形消息 Builder 每次新建 | 每 100ms 新建 2 个 Builder + 2 个 Message 对象 |
| A10 | 150-153 | 手动 `TaskCompletionSource + ct.Register` 阻塞 | MQTTnet `DisconnectedAsync` 事件驱动的重连可取代此模式；或使用 `Task.Delay(Timeout.Infinite, ct)` |
| A11 | 339 | `Task.Delay` 在波形循环 | 更改为 `PeriodicTimer`（.NET 6+）可减少 Timer 分配 |
| A12 | 369 | `new MqttClientDisconnectOptions()` 无配置 | 断开时应设置 Reason（如 NormalDisconnection）和 ReasonString，方便日志追踪 |
| A13 | 357-358 | 方法签名损坏 `public override async Task (CancellationToken ct)` | 缺失 `StopAsync` 方法名，编译可能报错 |
| A14 | 162-175 | `BuildRpcHandlerTable()` 每次重连调用 | Handler 映射表不随时间变化，应在构造函数中构建一次 |
| A15 | 290 | `_serviceProvider.GetRequiredService<UISharedBuffer>()` 在循环内 | 应提前在构造函数或循环外获取，避免每次重连都查 DI |

#### B. GC / 内存分配热点

| # | 行号 | 问题 | 影响 |
|---|------|------|------|
| B1 | 310-311 | `new byte[8000]` 每 100ms × 2 通道 | 16000 字节/100ms = 160 KB/s 持续分配 |
| B2 | 209-210 | `topic.Substring()` × 2 每次 RPC 请求 | 每次请求分配 2 个新字符串对象 |
| B3 | 196 | `Payload.ToArray()` | `ReadOnlySequence<byte>` → `byte[]` 分配新数组 |
| B4 | 200 | `RpcTopicPrefix + settings.MachineId + "/"` | 每次请求执行字符串拼接 |
| B5 | 138 | `RpcTopicPrefix + settings.MachineId + "/#"` | 每次重连执行字符串拼接 |
| B6 | 223 | `topic + "/response"` | 每次 RPC 请求新建字符串 |

#### C. 逻辑缺陷

| # | 行号 | 问题 | 说明 |
|---|------|------|------|
| C1 | 75-89 | 异常捕获范围过宽 | `while` 循环内捕获所有异常并重连，但 `ConnectAndServeAsync` 内部 `TaskCompletionSource` 等待取消信号时不抛异常 |
| C2 | 97-103 | 旧客户端清理不完整 | Disconnect 后立即 Dispose，但可能还有未完成的发布操作 |
| C3 | 145-156 | 波形 Task 先于取消信号返回 | `await tcs.Task` 在 `waveformTask` 之前，但波形循环内的 `ct` 与取消信号同一令牌 |

### 11.2 优化方案（完整替换版）

#### 11.2.1 核心架构变更

```
【当前】                                  【优化后】
BackgroundService.ExecuteAsync           BackgroundService.ExecuteAsync
  ├─ while 循环手动重连                   ├─ 连接一次，注册 DisconnectedAsync 事件
  ├─ ConnectAndServeAsync                 ├─ ConnectAndServeAsync (仅连接+订阅)
  ├─ TaskCompletionSource 阻塞            ├─ DisconnectedAsync 事件驱动状态机
  └─ 异常自动重连                         └─ 事件中判断是否需要重连
```

**重连机制**：利用 MQTTnet 的 `DisconnectedAsync` 事件。当连接断开时（无论预期/非预期），事件触发；在事件处理中根据 `MqttClientDisconnectedEventArgs.Reason` 判断是否需要自动重连，非正常断开则自动重连。

**阻塞方式**：`ConnectAndServeAsync` 不再内部阻塞。`ExecuteAsync` 中用 `DisconnectedAsync` 事件 + `TaskCompletionSource` 只在最终停止时才释放。

#### 11.2.2 具体优化项

##### A1: 用 `DisconnectedAsync` 事件替代手动重连循环

```csharp
// 【优化前】手动 while 循环
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    while (!stoppingToken.IsCancellationRequested)
    {
        try { await ConnectAndServeAsync(stoppingToken); }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(...), stoppingToken);
        }
    }
}

// 【优化后】事件驱动重连
private readonly SemaphoreSlim _reconnectLock = new(1, 1);
private bool _shouldReconnect = true;

protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    _mqttClient = new MqttClientFactory().CreateMqttClient();
    _mqttClient.DisconnectedAsync += OnDisconnectedAsync;
    _mqttClient.ApplicationMessageReceivedAsync += HandleRpcRequestAsync;
    
    await ConnectAsync(stoppingToken);
    
    // 等待停止信号
    try { await Task.Delay(Timeout.Infinite, stoppingToken); }
    catch (OperationCanceledException) { }
}
```

**提升**：利用 MQTTnet 原生事件，消除手动 while 循环和 `Task.Delay` 延迟；重连响应更快（事件触发而非轮询等待）。

##### A2-A5: 完善 MQTT 连接配置

```csharp
var optionsBuilder = new MqttClientOptionsBuilder()
    .WithTcpServer(host, port)
    .WithClientId(machineId)
    .WithCleanSession(true)                          // A3: 清除会话
    .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))    // A2: 心跳 30s
    .WithTimeout(TimeSpan.FromSeconds(10))            // A4: 连接超时 10s
    // A5: 遗嘱消息 — 进程崩溃时通知订阅者
    .WithWillTopic($"daq/{machineId}/events/state_changed")
    .WithWillPayload(willPayloadBytes)
    .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
    .WithWillRetain(true);
```

**提升**：心跳由 MQTTnet 自动管理无需手动编码；进程崩溃时 Broker 自动发布遗嘱消息通知订阅者。

##### A6: 使用 MQTT 5.0 原生 `ResponseTopic`

```csharp
// 【优化前】手动拼接响应主题
var responseTopic = topic + "/response";

// 【优化后】优先使用 MQTT 5.0 ResponseTopic
var responseTopic = eventArgs.ApplicationMessage.ResponseTopic;
if (string.IsNullOrEmpty(responseTopic))
{
    // MQTT 3.1.1 回退：手动拼接
    responseTopic = topic + "/response";
}
```

**提升**：MQTT 5.0 下零拼接开销；协议升级路径清晰。

##### A7: 预计算错误响应模板

```csharp
// 静态预计算
private static readonly byte[] ErrorUnknownMethodPrefix = 
    Encoding.UTF8.GetBytes("{\"success\":false,\"code\":\"UNKNOWN_METHOD\",\"message\":\"");
private static readonly byte[] ErrorUnknownMethodSuffix = 
    Encoding.UTF8.GetBytes("\"}");

// 使用时（仅需拼接中间的方法名）
var errorPayload = ConcatBytes(ErrorUnknownMethodPrefix, 
    Encoding.UTF8.GetBytes(methodName), ErrorUnknownMethodSuffix);
```

**提升**：避免每次错误创建完整 JSON 字符串，减少 `Encoding.UTF8.GetBytes()` 调用。

##### A8-A9: 预构建消息模板

```csharp
// 波形消息模板（仅 Topic + QOS 不变，Payload 每帧变化）
private MqttApplicationMessage _ch1MessageTemplate;
private MqttApplicationMessage _ch2MessageTemplate;

// 在连接后预构建
_ch1MessageTemplate = new MqttApplicationMessageBuilder()
    .WithTopic(ch1Topic)
    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce)
    .Build();

// 每帧仅更新 Payload（通过 WithPayload 创建新消息开销更小）
var msg1 = new MqttApplicationMessage
{
    Topic = _ch1MessageTemplate.Topic,
    QualityOfServiceLevel = _ch1MessageTemplate.QualityOfServiceLevel,
    PayloadSegment = ch1Bytes
};
```

**提升**：每次发布减少 Builder 对象分配。

##### A10: 用 `Task.Delay(Timeout.Infinite, ct)` 替代 TCS 模式

```csharp
// 【优化前】
var tcs = new TaskCompletionSource<bool>();
ct.Register(() => tcs.TrySetResult(true));
await tcs.Task;

// 【优化后】
try { await Task.Delay(Timeout.Infinite, ct); }
catch (OperationCanceledException) { /* 正常停止 */ }
```

**提升**：消除 `TaskCompletionSource` 分配和委托注册。

##### A11: `PeriodicTimer` 替代 `Task.Delay`

```csharp
// 【优化前】
while (!ct.IsCancellationRequested)
{
    // ... publish ...
    await Task.Delay(intervalMs, ct);
}

// 【优化后】
using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(intervalMs));
while (await timer.WaitForNextTickAsync(ct))
{
    // ... publish ...
}
```

**提升**：`PeriodicTimer` 内部复用单个计时器，避免每次 `Task.Delay` 创建新 Timer。

##### A14: Handler 路由表构建提前到构造函数

```csharp
public MqttRpcBackgroundService(...)
{
    // ... 保存依赖 ...
    _rpcHandlers = BuildRpcHandlerTable(); // 在构造函数中构建一次
}
```

##### B1: 波形缓冲区用 `ArrayPool<byte>` 复用

```csharp
private byte[] _ch1Bytes; // 复用，不每次 new
private byte[] _ch2Bytes;

// 初始化时从池中租用
private void InitializeWaveformBuffers()
{
    _ch1Bytes = ArrayPool<byte>.Shared.Rent(1000 * sizeof(double));
    _ch2Bytes = ArrayPool<byte>.Shared.Rent(1000 * sizeof(double));
}

// 停止时归还
public override void Dispose()
{
    if (_ch1Bytes != null) ArrayPool<byte>.Shared.Return(_ch1Bytes);
    if (_ch2Bytes != null) ArrayPool<byte>.Shared.Return(_ch2Bytes);
}
```

##### B2: 用 `ReadOnlySpan<char>` 避免 Substring

```csharp
// 【优化前】
var path = topic.Substring(prefix.Length);     // 分配新字符串
var slashIndex = path.IndexOf('/');
var methodName = path.Substring(0, slashIndex); // 再分配

// 【优化后】
var topicSpan = topic.AsSpan();
var pathSpan = topicSpan.Slice(prefix.Length);
var slashIndex = pathSpan.IndexOf('/');
var methodName = pathSpan.Slice(0, slashIndex).ToString(); // 仅此处分配
```

##### B5: 预计算订阅主题

```csharp
private readonly string _rpcSubscribeTopic; // 构造函数中预计算

public MqttRpcBackgroundService(...)
{
    var settings = _mqttSettings.CurrentValue;
    _rpcSubscribeTopic = $"{RpcTopicPrefix}{settings.MachineId}/#";
    _rpcTopicPrefix = $"{RpcTopicPrefix}{settings.MachineId}/";
}
```

### 11.3 优化后完整文件结构

```
MqttRpcBackgroundService : BackgroundService
│
├── 字段
│   ├── _serviceProvider, _mqttSettings, _eventPublisher, _logger
│   ├── _mqttClient (IMqttClient)
│   ├── _rpcHandlers (Dictionary, 构造函数中构建)
│   ├── _rpcTopicPrefix (预计算)
│   ├── _rpcSubscribeTopic (预计算)
│   ├── _waveformCh1Topic, _waveformCh2Topic (预计算)
│   ├── _waveformCh1Template, _waveformCh2Template (预构建消息模板)
│   ├── _waveformBuffer1, _waveformBuffer2 (double[], 复用)
│   ├── _ch1Bytes, _ch2Bytes (byte[], ArrayPool 租用)
│   ├── _reconnectLock (SemaphoreSlim, 防重入)
│   ├── _shouldReconnect (bool)
│   └── _stopTcs (TaskCompletionSource, 仅用于最终停止)
│
├── 方法
│   ├── 构造函数: 预计算主题 + 构建路由表 + 初始化波形缓冲
│   ├── ExecuteAsync(ct): 创建客户端 → 注册事件 → 连接 → 等待停止信号
│   ├── ConnectAsync(ct): 构建客户端选项 → 连接 → 订阅 → 启动波形循环
│   ├── OnDisconnectedAsync(args): 判断是否需要重连 → 自动重连
│   ├── HandleRpcRequestAsync(args): 解析请求 → 路由 → 发布响应
│   ├── PublishResponseAsync(topic, payload): 发布 RPC 响应
│   ├── PublishWaveformLoopAsync(ct): PeriodicTimer 驱动波形发布
│   ├── StopAsync(ct): 停止波形 → 发送遗嘱 → 断开 → 归还 ArrayPool
│   └── IDisposable: 归还 ArrayPool 缓冲区
└──
```

### 11.4 对性能/可靠性/可维护性的提升总结

| 维度 | 优化项 | 提升 |
|------|--------|------|
| 性能 | ArrayPool 复用波形字节数组 | 消除 160KB/s 的 GC 分配 |
| 性能 | 预计算主题字符串 | 消除每次请求/重连的字符串拼接 |
| 性能 | PeriodicTimer 替代 Task.Delay | 减少 Timer 对象分配 |
| 性能 | 预构建消息模板 | 减少 Builder 对象分配 |
| 可靠性 | KeepAlivePeriod 心跳 | 防止空闲连接被 Broker 断开 |
| 可靠性 | 遗嘱消息 | 进程崩溃自动通知订阅者 |
| 可靠性 | DisconnectedAsync 事件驱动重连 | 重连更快、逻辑更清晰 |
| 可靠性 | CleanSession | 避免 Broker 积压离线消息 |
| 可维护性 | Handler 表提前构建 | 减少重连时的 DI 查询 |
| 可维护性 | 删除 TCS 阻塞模式 | 代码更简洁，意图更明确 |
| 可维护性 | 使用 MQTT 5.0 ResponseTopic | 协议标准，向前兼容 |

### 11.5 实施注意事项

1. **`DisconnectedAsync` 事件重连**：需使用 `SemaphoreSlim` 防重入，避免并发重连
2. **`ArrayPool<byte>.Return()`**：归还前需确保缓冲区不再被使用（波形循环退出后归还）
3. **消息模板 Payload 更新**：直接设置 `MqttApplicationMessage.PayloadSegment` 属性而非 new Builder
4. **遗嘱消息 Payload**：需预构建系统状态的 JSON 字节数组（进程崩溃时无当前状态）
5. **`PeriodicTimer`**：.NET 6+ 可用，本项目 .NET 8 完全支持

