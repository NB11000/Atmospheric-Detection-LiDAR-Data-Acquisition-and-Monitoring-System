# Implementation Plan: SystemStateService 深模块重构

> Parent: [PRD: SystemStateService 深模块重构](PRD.md)
> Created: 2026-05-10

## Modules

| 模块 | 位置 | 单一职责 |
|------|------|---------|
| StateChangeEvents | `WebAPI/Models/` | 事件类型字符串常量集合，编译期消除拼写错误 |
| IMqttEventPublisher | `WebAPI/Service/` | MQTT 事件发布器的 PublishStateChangedAsync 接口 |
| ISignalRHubPublisher | `WebAPI/Service/` | SignalR 推送器的 PublishStateChangedAsync 接口 |
| SystemStateService | `WebAPI/Service/` | 系统状态缓存 + 路径 [A] 静默更新 + 路径 [B] 广播更新 + MQTT 连接状态变更自动广播 |
| GrpcServiceImpl | `WebAPI/Service/` | gRPC 双向流处理，分流调用 Silent/Broadcast，不再直接持有 Publisher |
| CniLaser | `CniLaserControl/` | 激光器串口控制，改用 Silent 更新状态，移除 Publisher 死代码 |
| Program | `WebAPI/` | DI 注册：SystemStateService 新增 Lazy<IMqttEventPublisher> + ISignalRHubPublisher |

## Interfaces

### StateChangeEvents

```csharp
public static class StateChangeEvents
{
    // 采集卡
    public const string CollectorConnected    = "collector_connected";
    public const string CollectorDisconnected = "collector_disconnected";
    public const string DeviceOpened          = "device_opened";
    public const string DeviceClosed          = "device_closed";
    public const string AcquisitionStarted    = "acquisition_started";
    public const string AcquisitionStopped    = "acquisition_stopped";
    public const string DeviceDisconnected    = "device_disconnected";
    public const string AcquisitionFailed     = "acquisition_failed";
    public const string DeviceOpenFailed      = "device_open_failed";

    // 激光器
    public const string LaserConnected        = "laser_connected";
    public const string LaserDisconnected     = "laser_disconnected";
    public const string LaserOn               = "laser_on";
    public const string LaserOff              = "laser_off";

    // 系统
    public const string Error                 = "error";
    public const string MqttConnected         = "mqtt_connected";
    public const string MqttDisconnected      = "mqtt_disconnected";
}
```

### IMqttEventPublisher

```csharp
public interface IMqttEventPublisher
{
    Task PublishStateChangedAsync(string eventType, string source, string reason, string message);
}
```

### ISignalRHubPublisher

```csharp
public interface ISignalRHubPublisher
{
    Task PublishStateChangedAsync(string eventType, string source, string reason, string message);
}
```

### SystemStateService（新成员）

```csharp
// 路径 [A]：静默更新（仅缓存）
public void UpdateCollectorStateSilent(Func<CollectorStateDto, CollectorStateDto> updater);
public void UpdateLaserStateSilent(Func<LaserStateDto, LaserStateDto> updater);

// 路径 [B]：广播更新（缓存 + 双通道广播）
public void UpdateCollectorStateAndBroadcast(
    Func<CollectorStateDto, CollectorStateDto> updater,
    string eventType, string reason);
public void UpdateLaserStateAndBroadcast(
    Func<LaserStateDto, LaserStateDto> updater,
    string eventType, string reason);
public void ResetCollectorStateAndBroadcast(string reason);

// 增强：MQTT 连接状态变更（内部自动广播）
public void UpdateMqttConnectionState(bool isConnected);
// 断开时：MqttConnectionStateChanged.Invoke(false) + SignalR mqtt_disconnected
// 恢复时：MqttConnectionStateChanged.Invoke(true) + MQTT/SignalR mqtt_connected + State 快照

// 不变
public SystemStateDto GetSystemState();
public CollectorStateDto GetCollectorState();
public LaserStateDto GetLaserState();
public event Action<bool>? AcquiringStateChanged;
public event Action<bool>? MqttConnectionStateChanged;
```

### SystemStateService 构造函数变更

```csharp
// 改造前
public SystemStateService(ILogger<SystemStateService> logger)

// 改造后
public SystemStateService(
    ILogger<SystemStateService> logger,
    Lazy<IMqttEventPublisher> mqttEventPublisher,
    ISignalRHubPublisher signalRHubPublisher)
```

### GrpcServiceImpl 构造函数变更

```csharp
// 改造前（注入 _mqttEventPublisher + _hubPublisher）
public GrpcServiceImpl(ILogger<GrpcServiceImpl> logger, IServiceProvider serviceProvider,
    SystemStateService stateService, SignalRHubPublisher hubPublisher,
    MqttEventPublisher mqttEventPublisher, DetectionPublisherService detectionPublisher)

// 改造后（移除 _mqttEventPublisher + _hubPublisher 参数和字段）
public GrpcServiceImpl(ILogger<GrpcServiceImpl> logger, IServiceProvider serviceProvider,
    SystemStateService stateService, DetectionPublisherService detectionPublisher)
```

### CniLaser 构造函数变更

```csharp
// 改造前
public CniLaser(ILogger<CniLaser> logger, IServiceProvider serviceProvider,
    SignalRHubPublisher signalRHubPublisher, MqttEventPublisher mqttEventPublisher)

// 改造后
public CniLaser(ILogger<CniLaser> logger, IServiceProvider serviceProvider)
```

## Data Flow

### 路径 [A]：MQTT RPC 命令 → Silent 更新

```
MQTT RPC 请求 → Handler → gRPC/CniLaser → command_response
→ GrpcServiceImpl.UpdateStateFromCommandResponse()
→ SystemStateService.UpdateCollectorStateSilent()
  ├── 更新 volatile _cachedCollectorState
  ├── Acquiring 变化 → AcquiringStateChanged.Invoke()
  └── 不触发 MQTT/SignalR 广播
```

### 路径 [B]：异常事件 → Broadcast 更新

```
gRPC 双向流异常 / Error 消息 / 设备断开
→ GrpcServiceImpl
→ SystemStateService.UpdateCollectorStateAndBroadcast(updater, eventType, reason)
  ├── 更新 volatile _cachedCollectorState
  ├── Acquiring 变化 → AcquiringStateChanged.Invoke()
  ├── _ = _mqttEventPublisher.Value.PublishStateChangedAsync(eventType, "collector", reason, ...)
  └── _ = _signalRHubPublisher.PublishStateChangedAsync(eventType, "collector", reason, ...)
```

### MQTT 断连 → SignalR 广播

```
MqttRpcBackgroundService.OnDisconnectedAsync
→ SystemStateService.UpdateMqttConnectionState(false)
  ├── _mqttConnected = false
  ├── MqttConnectionStateChanged.Invoke(false)
  ├── _ = _signalRHubPublisher.PublishStateChangedAsync("mqtt_disconnected", ...)
  └── MQTT 推送跳过（通道不可用）
```

### MQTT 恢复 → 双通道广播 + 快照补偿

```
MqttRpcBackgroundService.ConnectAsync
→ _mqttClient.ConnectAsync() → IsConnected = true
→ SystemStateService.UpdateMqttConnectionState(true)
  ├── _mqttConnected = true
  ├── MqttConnectionStateChanged.Invoke(true)
  ├── var snapshot = GetSystemState()
  ├── _ = _mqttEventPublisher.Value.PublishStateChangedAsync("mqtt_connected", State=snapshot, ...)
  └── _ = _signalRHubPublisher.PublishStateChangedAsync("mqtt_connected", State=snapshot, ...)
```

## Key Technical Decisions

| 决策 | 选择 | 原因 | 拒绝方案 |
|------|------|------|---------|
| 方法拆分 vs 枚举参数 | 方法拆分（Silent/AndBroadcast） | 方法名即文档；Silent 无需 eventType/reason 参数 | 枚举参数导致 Silent 模式下无效参数污染 |
| SystemStateService 持有 Publisher | 直接持有 | 两个 Publisher 是唯一下游，中间层增加复杂度无收益 | 内部事件 + 独立订阅者：额外类、事件时序、GC 压力 |
| 推送失败处理 | fire-and-forget（`_ =`）+ try/catch | 不阻塞状态更新；MQTT QoS 1 保证投递 | await 会阻塞状态更新线程 |
| 循环依赖 | `Lazy<IMqttEventPublisher>` | .NET DI 原生支持，语义清晰 | 重构接口让 MqttEventPublisher 不依赖 SystemStateService：改动面更大 |
| 错误 eventType 粒度 | 每个 error code 独立 eventType | 远端可按类型精确路由和告警分级 | 通用 "Error"：所有订阅方需解析 reason 字符串 |
| 测试 mock 策略 | 提取 IMqttEventPublisher / ISignalRHubPublisher 接口 | 轻量接口（仅 PublishStateChangedAsync），TDD 可验证核心契约 | 依赖具体类：mock 成本高，SignalRHubPublisher 需 IHubContext |

## Test Strategy

### SystemStateService 单元测试（Mock）

| 测试用例 | 验证点 |
|---------|--------|
| UpdateCollectorStateSilent_UpdatesCache_DoesNotPublish | 状态缓存更新；IMqttEventPublisher / ISignalRHubPublisher 无调用 |
| UpdateLaserStateSilent_UpdatesCache_DoesNotPublish | 同上 |
| UpdateCollectorStateAndBroadcast_UpdatesCacheAndPublishesBothChannels | 状态缓存更新；Mock 验证 PublishStateChangedAsync 各被调用 1 次，参数正确 |
| UpdateLaserStateAndBroadcast_UpdatesCacheAndPublishesBothChannels | 同上 |
| ResetCollectorStateAndBroadcast_ResetsToDefaultAndPublishes | 缓存重置为默认值；双通道推送被调用 |
| UpdateMqttConnectionState_True_AfterFalse_PublishesBothChannels | MQTT+SignalR 均推送 mqtt_connected + State 快照 |
| UpdateMqttConnectionState_False_PublishesOnlySignalR | 仅 SignalR 推送 mqtt_disconnected，MQTT 无调用 |
| UpdateMqttConnectionState_SameValue_NoOp | 推送无调用；内部事件无触发 |
| UpdateCollectorStateAndBroadcast_AcquiringChanged_FiresInternalEvent | Acquiring 从 false→true 或 true→false 时 AcquiringStateChanged 触发 |
| UpdateCollectorStateSilent_AcquiringChanged_FiresInternalEvent | Silent 也触发 AcquiringStateChanged |

### GrpcServiceImpl 单元测试

| 测试用例 | 验证点 |
|---------|--------|
| CommandResponse_CallsUpdateSilent | command_response 到达时调 UpdateCollectorStateSilent，不调 AndBroadcast |
| DeviceDisconnectedError_CallsUpdateAndBroadcast | DEVICE_DISCONNECTED error → UpdateCollectorStateAndBroadcast(device_disconnected) |
| AcquisitionFailedError_CallsUpdateAndBroadcast | ACQUISITION_FAILED error → UpdateCollectorStateAndBroadcast(acquisition_failed) |
| DeviceOpenFailedError_CallsUpdateAndBroadcast | DEVICE_OPEN_FAILED error → UpdateCollectorStateAndBroadcast(device_open_failed) |
| GrpcConnect_CallsUpdateAndBroadcast | gRPC 连接建立 → UpdateCollectorStateAndBroadcast(collector_connected) |
| GrpcDisconnect_CallsResetAndBroadcast | gRPC 断开 → ResetCollectorStateAndBroadcast(collector_disconnected) |

### 不做测试的项

- MqttEventPublisher / SignalRHubPublisher 的实际网络行为（集成测试范围）
- CniLaser 的修改（纯删除死代码，无新逻辑）
- Program.cs DI 变更（.NET DI 容器正确性由运行时保证）

## Vertical Slice Design

| Slice | 内容 | 依赖 | 类型 |
|-------|------|------|------|
| 1 | StateChangeEvents 常量类 + IMqttEventPublisher / ISignalRHubPublisher 接口 | None | AFK |
| 2 | SystemStateService Silent 方法 + 单元测试 | Slice 1 | AFK |
| 3 | SystemStateService AndBroadcast 方法 + 单元测试 | Slice 2 | AFK |
| 4 | SystemStateService UpdateMqttConnectionState 增强（广播+快照补偿）+ 单元测试 | Slice 3 | AFK |
| 5 | GrpcServiceImpl 分流重构（UpdateStateFromCommandResponse→Silent, UpdateStateFromError→AndBroadcast, 连接/断开→AndBroadcast） | Slice 4 | AFK |
| 6 | CniLaser 死代码清理 + Silent 迁移 | Slice 4 | AFK |
| 7 | Program.cs DI 注册更新 + GrpcServiceImpl/CniLaser 构造函数清理 | Slice 5, 6 | AFK |
| 8 | [Obsolete] 旧方法移除 | Slice 7 | AFK |
