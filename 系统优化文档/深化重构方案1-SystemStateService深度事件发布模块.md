# 深化重构方案 1：将 SystemStateService 深化为状态监控与事件发布模块

> 文档版本：v2.0
> 创建日期：2026-05-10
> 更新：根据"强乐观 UI + 命令链路正确性"架构约定修正设计方案
> 状态：方案确认，待实施
> 关联问题报告：`问题记录/设备运行状态监控机制缺失问题分析.md`

---

## 一、问题背景

系统的前后端交互遵循"强乐观 UI + 命令链路正确性"架构约定：

- **路径 [A] — MQTT RPC 命令链路变更**：命令响应已携带确认，调用方乐观更新 UI，**不需要 `events/state_changed` 广播**。
- **路径 [B] — 命令链路之外的异常变更**：设备异常断开、硬件故障、检测告警等，无命令响应作为确认载体，**必须主动广播**。

当前 `SystemStateService` 的接口**不区分这两种路径**，导致路径 [B] 的广播决策被推到了每一个调用方身上。

### 1.1 现状：路径 [B] 推送调用分布

```
GrpcServiceImpl（路径B覆盖：部分工作）
    ├── 连接时：推 MQTT ✓  推 SignalR ✗
    ├── 断开时：推 MQTT ✓  推 SignalR ✓
    └── 错误时：推 MQTT ✓  推 SignalR ✓

CniLaser（路径B覆盖：完全缺失）
    ├── MQTT 推送：全部被注释掉 ✗
    └── SignalR 推送：仅 PublishLaserStateChangedAsync

SystemStateService（路径B覆盖：MQTT断连无广播）
    └── 仅触发内部事件，不对外广播

DetectionPublisherService（路径B覆盖：告警与状态割裂）
    └── 告警 → detection/alerts ✓  但 → state_changed ✗
```

**路径 [B] 的 9 种场景中，仅 5 种有 MQTT 覆盖，4 种完全缺失。**

### 1.2 根本原因

`SystemStateService` 是一个**浅模块（shallow module）**。它的接口（7 个公共成员）几乎就是实现本身：

| 成员 | 类型 | 职责 |
|---|---|---|
| `UpdateCollectorState(Func<...>)` | 方法 | 更新采集卡状态值 |
| `UpdateLaserState(Func<...>)` | 方法 | 更新激光器状态值 |
| `ResetCollectorState()` | 方法 | 重置采集卡状态值 |
| `UpdateMqttConnectionState(bool)` | 方法 | 更新 MQTT 连接状态值 |
| `GetSystemState()` / `GetCollectorState()` / `GetLaserState()` | 方法 | 读取状态快照 |
| `AcquiringStateChanged` | 事件 | 仅采集状态变更 |
| `MqttConnectionStateChanged` | 事件 | 仅 MQTT 连接状态变更 |

**7 个成员，但"状态变更后自动对外发布"的能力为零。** 调用方必须知道"更新状态后还要手动推送"这个隐含约定，导致覆盖不完整、行为不一致。

---

## 二、优化目标

将 `SystemStateService` 从"只管理状态值"的浅模块，深化为区分**两条更新路径**的**深模块（deep module）**。

核心原则：
- **路径 [A] — 静默更新（Silent Update）**：仅更新缓存，不广播。供 MQTT RPC Handler 在命令成功后调用。（命令响应本身就是确认载体）
- **路径 [B] — 广播更新（Broadcast Update）**：更新缓存 + 自动广播到 MQTT + SignalR。供异常事件（gRPC 断连、硬件故障、检测告警等）调用
- **调用方只需选择路径**：不必知道 MqttEventPublisher 和 SignalRHubPublisher 的存在

---

## 三、方案设计

### 3.1 核心思路

`SystemStateService` 提供两组更新方法，对应两条路径：

- **`UpdateCollectorStateSilent()` / `UpdateLaserStateSilent()`** → 路径 [A]，仅更新缓存
- **`UpdateCollectorStateAndBroadcast()` / `UpdateLaserStateAndBroadcast()`** → 路径 [B]，更新缓存 + 双通道广播

`SystemStateService` 内部持有 `MqttEventPublisher` 和 `SignalRHubPublisher` 的引用。路径 [B] 的方法自动比较新旧状态差异，生成事件描述，调用两个发布通道。

### 3.2 改造前架构

```
CniLaser                    GrpcServiceImpl
    │                             │
    ├── UpdateLaserState()        ├── UpdateCollectorState()
    │     (更新缓存)               │     (更新缓存)
    │                             │
    ├── PublishLaserStateChanged  ├── PublishStateChangedAsync
    │     → SignalR ✓             │     → MQTT ✓ (部分)
    │     → MQTT ✗ (注释掉)       │     → SignalR ✓ (部分)
    │                             │
    └── (路径B MQTT推送缺失!)     └── (行为不一致!)
```

### 3.3 改造后架构

```
路径 [A] — MQTT RPC Handler（命令链路，静默更新）

  CollectorHandler / LaserHandler
      │
      │ 命令已确认成功
      │
      ├── UpdateCollectorStateSilent(newState)
      └── UpdateLaserStateSilent(newState)
      (仅更新缓存，不广播 —— 命令响应已携带确认)


路径 [B] — GrpcServiceImpl / CniLaser / DetectionPublisherService（异常事件）

  GrpcServiceImpl              CniLaser（硬件事件）
      │                             │
      │ gRPC断连/错误上报           │ 串口意外断开
      │                             │
      ▼                             ▼
  ┌─────────────────────────────────────────┐
  │        SystemStateService               │
  │                                         │
  │  [路径A] SilentUpdate:                  │
  │    UpdateCollectorStateSilent()         │
  │    UpdateLaserStateSilent()             │
  │      └── 仅更新缓存                     │
  │                                         │
  │  [路径B] BroadcastUpdate:               │
  │    UpdateCollectorStateAndBroadcast(    │
  │        updater, eventType, reason)      │
  │    UpdateLaserStateAndBroadcast(        │
  │        updater, eventType, reason)      │
  │      ├── 更新缓存                       │
  │      ├── 比较新旧差异，生成事件描述      │
  │      ├── → MqttEventPublisher           │
  │      └── → SignalRHubPublisher          │
  └─────────────────────────────────────────┘
```

### 3.4 接口变更

#### SystemStateService 新接口

```csharp
public class SystemStateService
{
    // === 保留的读取接口（不变）===
    public SystemStateDto GetSystemState();
    public CollectorStateDto GetCollectorState();
    public LaserStateDto GetLaserState();

    // === 路径 [A]：静默更新（供 MQTT RPC Handler 使用）===

    /// <summary>
    /// 静默更新采集卡状态（仅缓存，不广播）
    /// 适用场景：MQTT RPC 命令响应成功后，命令响应已携带确认，无需 state_changed 广播
    /// </summary>
    public void UpdateCollectorStateSilent(Func<CollectorStateDto, CollectorStateDto> updater);

    /// <summary>
    /// 静默更新激光器状态（仅缓存，不广播）
    /// 适用场景：MQTT RPC laser-connect/disconnect/on/off 命令成功后
    /// </summary>
    public void UpdateLaserStateSilent(Func<LaserStateDto, LaserStateDto> updater);

    // === 路径 [B]：广播更新（供异常事件调用方使用）===

    /// <summary>
    /// 广播更新采集卡状态（更新缓存 + 双通道推送 state_changed 事件）
    /// 适用场景：gRPC 断连、设备异常断开、采集异常终止等非命令链路变更
    /// </summary>
    public void UpdateCollectorStateAndBroadcast(
        Func<CollectorStateDto, CollectorStateDto> updater,
        string eventType,
        string reason);

    /// <summary>
    /// 广播更新激光器状态（更新缓存 + 双通道推送 state_changed 事件）
    /// 适用场景：激光器硬件意外断开等非命令链路变更
    /// </summary>
    public void UpdateLaserStateAndBroadcast(
        Func<LaserStateDto, LaserStateDto> updater,
        string eventType,
        string reason);

    /// <summary>
    /// 重置采集卡状态并广播（路径 [B]）
    /// </summary>
    public void ResetCollectorStateAndBroadcast(string reason);

    // === 保留的内部事件（供 AcquisitionLifecycleCoordinator 等内部订阅方使用）===
    public event Action<bool>? AcquiringStateChanged;     // Acquiring 状态变更时触发
    public event Action<bool>? MqttConnectionStateChanged; // MQTT 连接状态变更时触发
}
```

#### 调用方代码示例

**路径 [A] — MQTT RPC Handler：静默更新**

```csharp
// CollectorHandler.HandleOpenDevice() — MQTT RPC collector-open-device
var response = await _grpcService.SendCommandToClientAndWaitResponse(ClientId, "OPEN_DEVICE");
// 命令已成功响应 → 静默更新缓存，不需要广播
_stateService.UpdateCollectorStateSilent(current => new CollectorStateDto
{
    ProcessConnected = true,
    DeviceOpened = response.MHandle > 0,
    Acquiring = current.Acquiring,
    Handle = response.MHandle,
    LastMessage = response.Content
});
// RPC 响应已返回给调用方，state_changed 广播不需要
```

**路径 [B] — GrpcServiceImpl：广播更新**

```csharp
// gRPC 双向流建立，采集子进程连接（非命令链路）
_stateService.UpdateCollectorStateAndBroadcast(
    _ => new CollectorStateDto { ProcessConnected = true, ... },
    eventType: "collector_connected",
    reason: "采集子进程 gRPC 连接已建立");

// gRPC 双向流断开（非命令链路）
_stateService.ResetCollectorStateAndBroadcast("采集子进程 gRPC 连接已断开");

// 子进程上报 DEVICE_DISCONNECTED 错误（非命令链路）
_stateService.UpdateCollectorStateAndBroadcast(
    current => new CollectorStateDto { ProcessConnected = true, DeviceOpened = false, ... },
    eventType: "device_disconnected",
    reason: "设备异常断开：USB 读取失败");
```

**路径 [B] — CniLaser：广播更新（之前注释掉的 MQTT 推送）**

```csharp
// 激光器串口意外断开（硬件事件检测到）
_stateService.UpdateLaserStateAndBroadcast(
    state => new LaserStateDto { SerialConnected = false, EmissionOn = false, ... },
    eventType: "laser_disconnected",
    reason: "激光器串口意外断开");
```

### 3.5 状态变更事件类型枚举

统一的事件类型定义，取代当前分散的字符串：

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

---

## 四、改造范围

### 4.1 需修改的文件

| 文件 | 改造内容 |
|---|---|
| `WebAPI/Service/SystemStateService.cs` | 新增 `Silent` / `AndBroadcast` 两组方法；内部持有发布器引用；路径 [B] 自动双通道广播 |
| `WebAPI/Service/GrpcServiceImpl.cs` | 将现有 `UpdateCollectorState` 调用分流：命令响应 → `Silent`；gRPC 连接/断开/错误 → `AndBroadcast`；删除直接调用 `MqttEventPublisher` / `SignalRHubPublisher` 的代码 |
| `WebAPI/Service/CniLaser.cs` | 新增硬件事件检测（意外断开）；MQTT RPC 调用的主动操作 → `Silent`；硬件事件 → `AndBroadcast`；删除 `PublishLaserStateChangedAsync` 和被注释的 MQTT 调用 |
| `WebAPI/MqttRpc/CollectorHandler.cs` | `HandleOpenDevice` / `HandleCloseDevice` / `HandleStartAd` / `HandleStopAd` 等：命令成功 → `Silent` 更新 |
| `WebAPI/MqttRpc/LaserHandler.cs` | `HandleConnect` / `HandleDisconnect` / `HandleLaserOn` / `HandleLaserOff`：命令成功 → `Silent` 更新 |
| `WebAPI/Program.cs` | 调整 DI 注册：`SystemStateService` 需注入 `MqttEventPublisher` 和 `SignalRHubPublisher` |

### 4.2 不需修改的文件

| 文件 | 原因 |
|---|---|
| `WebAPI/Service/AcquisitionLifecycleCoordinator.cs` | 仍通过 `AcquiringStateChanged` / `MqttConnectionStateChanged` 事件订阅，接口不变 |
| `WebAPI/Service/WaveformPublishService.cs` 等采集绑定服务 | 不直接调用状态更新，不受影响 |
| `WebAPI/MqttRpc/CollectorHandler.cs` / `LaserHandler.cs` | 通过 gRPC 间接触发状态变更，不直接调 `SystemStateService` 更新方法 |
| `WebAPI/Service/MqttEventPublisher.cs` | 保留 `PublishStateChangedAsync` 公共方法（供 `SystemStateService` 内部调用），但外部调用方全部移除 |

---

## 五、收益分析

### 5.1 架构收益

| 维度 | 改造前 | 改造后 |
|---|---|---|
| **Locality** | 路径 [B] 推送逻辑分散在 4 个调用方 | 路径 [B] 广播逻辑集中在 `AndBroadcast` 方法中 |
| **路径区分** | 接口不区分 [A]/[B]，决策推给调用方 | `Silent` vs `AndBroadcast` 在接口层面区分，调用方只需选路径 |
| **接口深度** | 7 个公共成员，推送能力为零（浅模块） | 3 读取 + 2 Silent + 3 Broadcast，接口小而能力大（深模块） |
| **路径 [B] MQTT 覆盖率** | 5/9 场景覆盖，激光器全部缺失 | 9/9 场景全覆盖 |
| **一致性** | MQTT 和 SignalR 覆盖不对称 | 路径 [B] 双通道同步推送，由模块保证 |
| **扩展安全** | 新增路径 [B] 场景时，开发者可能遗漏推送 | 调用 `AndBroadcast` 即自动获得完整广播能力 |

### 5.2 测试收益

- **路径 [A] vs [B] 可分别验证**：注入 mock 发布器，验证 `Silent` 不触发发布，`AndBroadcast` 必然触发
- **调用方测试简化**：`GrpcServiceImpl` 和 `CniLaser` 的测试不再需要 mock 发布器——它们只调 `SystemStateService`，发布行为由 `SystemStateService` 自身的测试覆盖
- **减少集成测试依赖**：不再需要完整 gRPC 链路来验证"异常事件是否正确推送"

### 5.3 删除测试

改造后，如果删除 `SystemStateService`：
- 状态缓存消失 → 所有调用方需自行管理
- 路径 [A] / [B] 区分消失 → 调用方需自行判断
- 双通道广播能力消失 → 调用方需各自集成 `MqttEventPublisher` + `SignalRHubPublisher`

**这就是深模块的标志：删除它，整个系统的能力缺口远大于代码量。**

---

## 六、风险与缓解

| 风险 | 可能性 | 影响 | 缓解措施 |
|---|---|---|---|
| `eventType` 字符串硬编码容易拼写错误 | 中 | 低 | 使用 `StateChangeEvents` 常量类，编译期检查 |
| `SystemStateService` 持有发布器引用增加耦合 | 低 | 低 | 通过 DI 注入接口/抽象，测试中可替换为 mock |
| MQTT 推送失败影响状态更新 | 低 | 中 | 推送使用 try/catch + `_ =`（fire-and-forget），不阻塞状态更新 |
| `MqttEventPublisher.MqttClient` 可能为 null | 低 | 低 | `MqttEventPublisher` 内部已有 null 检查和跳过逻辑 |

---

## 七、实施步骤

### 第一阶段：修改 SystemStateService

1. 在构造函数中注入 `MqttEventPublisher` 和 `SignalRHubPublisher`
2. 新增 `UpdateCollectorStateSilent` / `UpdateLaserStateSilent`（直接调用现有 volatile 更新逻辑）
3. 新增 `UpdateCollectorStateAndBroadcast` / `UpdateLaserStateAndBroadcast` / `ResetCollectorStateAndBroadcast`（更新 + 自动双通道广播）
4. 新增 `StateChangeEvents` 常量类
5. 保留现有 `UpdateCollectorState` / `UpdateLaserState` / `ResetCollectorState` 作为过渡（标记 `[Obsolete]`），内部委托给 `Silent` 版本

### 第二阶段：分流调用方

1. **GrpcServiceImpl**：gRPC 连接/断开/错误 → `AndBroadcast`；命令响应（`UpdateStateFromCommandResponse`）→ `Silent`
2. **CollectorHandler / LaserHandler**：命令成功 → `Silent`
3. **CniLaser**：MQTT RPC 调用的主动操作 → `Silent`；新增硬件事件检测 → `AndBroadcast`

### 第三阶段：清理

1. 删除 `GrpcServiceImpl` 中直接调用 `_mqttEventPublisher.PublishStateChangedAsync` 和 `_hubPublisher.PublishStateChangedAsync` 的代码
2. 删除 `CniLaser.PublishLaserStateChangedAsync` 方法和被注释的 MQTT 调用
3. 移除 `GrpcServiceImpl` 和 `CniLaser` 对 `MqttEventPublisher` / `SignalRHubPublisher` 的 DI 依赖
4. 移除 `SystemStateService` 中的 `[Obsolete]` 过渡方法
5. 更新 `Program.cs` DI 注册

---

**文档信息**
- 创建时间：2026-05-10
- 相关文件：
  - `WebAPI/Service/SystemStateService.cs`
  - `WebAPI/Service/GrpcServiceImpl.cs`
  - `WebAPI/Service/CniLaser.cs`
  - `WebAPI/Service/MqttEventPublisher.cs`
  - `WebAPI/Service/SignalRHubPublisher.cs`
  - `WebAPI/Program.cs`
- 状态：方案确认，待实施
