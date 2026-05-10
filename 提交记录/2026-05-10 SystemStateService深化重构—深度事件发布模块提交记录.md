# 提交记录

> 生成时间：2026-05-10 18:30
> 仓库：数据采集与检测系统 V2.0
> 分支：`main`

---

## 一、背景（Background）

`SystemStateService` 原为浅模块：仅维护采集卡/激光器/MQTT 连接的状态值缓存。状态变更的 MQTT/SignalR 双通道广播逻辑散落在 `GrpcServiceImpl`（手写 MQTT + SignalR 推送）和 `CniLaser`（死代码 `PublishLaserStateChangedAsync` + 注释掉的 MQTT 推送）中，导致三个问题。

### 问题（Problem）

#### 1. 事件发布职责分散

状态更新与事件推送耦合在调用方：`GrpcServiceImpl` 的 Connect 路径、Error 路径、Disconnect 路径各自手写 MQTT/SignalR 推送代码，总计约 40 行分散的重复推送逻辑。每次新增状态变更场景都需在各调用方重复写双通道推送。

#### 2. 缺少分路径语义

项目中实际存在两条更新路径，但代码未加区分。路径 [A] 为 MQTT RPC 命令响应链路（`command_response` → 状态已由命令响应确认 → 无需广播）；路径 [B] 为异常事件链路（设备断开、采集失败 → 需广播通知所有订阅方）。原 `UpdateCollectorState` 统一处理两条路径，调用方需自行判断是否需要推送。

#### 3. CniLaser 死代码和循环依赖风险

`CniLaser.PublishLaserStateChangedAsync()` 为死代码（无任何调用点），3 处 MQTT 推送被注释掉但仍保留 `_mqttEventPublisher` 依赖。`SystemStateService` 与 `MqttEventPublisher` 存在循环依赖（`SystemStateService` → `MqttEventPublisher` → `SystemStateService`）。

---

## 二、解决方案（Solution）

### 整体思路

将 `SystemStateService` 从"状态值缓存"深化为"状态管理 + 自动双通道事件广播"的深模块：调用方仅需选择 Silent（路径 A）或 AndBroadcast（路径 B），内部自动完成 MQTT + SignalR 推送。通过 `Lazy<T>` 打破循环依赖，提取 `IMqttEventPublisher` / `ISignalRHubPublisher` 接口支撑 TDD 测试。

### 具体实施

#### 1. 事件类型常量提取 (`StateChangeEvents`)

16 个事件类型字符串收拢为 `StateChangeEvents` 静态类常量，消除散落文件中的字符串字面量：`CollectorConnected`、`DeviceDisconnected`、`AcquisitionStarted`、`LaserOn`、`MqttConnected` 等。

#### 2. 接口提取支撑 TDD

`IMqttEventPublisher` 和 `ISignalRHubPublisher` 各含单方法 `PublishStateChangedAsync(eventType, source, reason, message)`。`MqttEventPublisher` 和 `SignalRHubPublisher` 实现对应接口，测试中使用 Mock 替代真实实现。

#### 3. SystemStateService 深模块方法

**路径 A — Silent（静默更新，仅缓存）**：
- `UpdateCollectorStateSilent(Func<CollectorStateDto, CollectorStateDto>)` — 更新缓存，Acquiring 变化时触发 `AcquiringStateChanged` 内部事件
- `UpdateLaserStateSilent(Func<LaserStateDto, LaserStateDto>)` — 同上

**路径 B — AndBroadcast（更新 + 双通道广播）**：
- `UpdateCollectorStateAndBroadcast(updater, eventType, reason)` — Silent + `BroadcastAsync` 双通道推送
- `UpdateLaserStateAndBroadcast(updater, eventType, reason)` — 同上
- `ResetCollectorStateAndBroadcast(reason)` — 重置为默认值 + 双通道推送 `collector_disconnected`

**MQTT 连接状态**：
- `UpdateMqttConnectionState(true)` → 双通道广播 `mqtt_connected` + 快照补偿
- `UpdateMqttConnectionState(false)` → 仅 SignalR 广播 `mqtt_disconnected`（MQTT 通道不可用）

**私有方法**：
- `BroadcastAsync(eventType, source, reason, message)` — `_ =` 发后即忘，try/catch 包裹每个发布者，推送失败不破坏状态

#### 4. `Lazy<T>` 打破循环依赖

`SystemStateService` 构造函数接受 `Lazy<IMqttEventPublisher>`，DI 容器在首次访问 `.Value` 时才解析 `MqttEventPublisher`，避免构造函数循环。

```csharp
public SystemStateService(ILogger<SystemStateService> logger,
    Lazy<IMqttEventPublisher> mqttEventPublisher,
    ISignalRHubPublisher signalRHubPublisher)
```

#### 5. GrpcServiceImpl 调用方分流

| 位置 | 原方法 | 新方法 | 路径 |
|------|--------|--------|------|
| Connect | `UpdateCollectorState` + 手动 MQTT 推送 | `UpdateCollectorStateAndBroadcast(CollectorConnected)` | [B] |
| command_response | `UpdateCollectorState` | `UpdateCollectorStateSilent` | [A] |
| Error: DEVICE_DISCONNECTED | `UpdateCollectorState` | `UpdateCollectorStateAndBroadcast(DeviceDisconnected)` | [B] |
| Error: ACQUISITION_FAILED | `UpdateCollectorState` | `UpdateCollectorStateAndBroadcast(AcquisitionFailed)` | [B] |
| Error: DEVICE_OPEN_FAILED | `UpdateCollectorState` | `UpdateCollectorStateAndBroadcast(DeviceOpenFailed)` | [B] |
| Error: default | `UpdateCollectorState` | `UpdateCollectorStateSilent` | [A] |
| Disconnect | `ResetCollectorState` + 手动 MQTT + 手动 SignalR | `ResetCollectorStateAndBroadcast` | [B] |

#### 6. CniLaser 死代码清理

- 删除 `PublishLaserStateChangedAsync` 方法（无调用点）
- 删除 `_hubPublisher` / `_mqttEventPublisher` 字段及构造函数参数
- 删除 3 处已注释的 `_mqttEventPublisher.PublishStateChangedAsync` 调用
- `UpdateLaserStateCache` 改用 `UpdateLaserStateSilent`

#### 7. DI 接口转发注册

```csharp
builder.Services.AddSingleton<ISignalRHubPublisher>(sp => sp.GetRequiredService<SignalRHubPublisher>());
builder.Services.AddSingleton<IMqttEventPublisher>(sp => sp.GetRequiredService<MqttEventPublisher>());
```

确保 `SystemStateService` 三参数构造函数可被 DI 容器解析，同时保留具体类型直接解析。

#### 8. 删除 [Obsolete] 过渡方法

移除 `UpdateCollectorState`、`UpdateLaserState`、`ResetCollectorState` 三个 `[Obsolete]` 过渡方法。`AcquisitionLifecycleCoordinatorTests` 中引用统一更新为 `UpdateCollectorStateSilent`。

---

## 三、Git 提交消息

```
feat: SystemStateService深化重构为深度事件发布模块

1. 新增 StateChangeEvents 16个事件类型常量，消除字符串字面量散落
2. 提取 IMqttEventPublisher/ISignalRHubPublisher 接口，MqttEventPublisher/SignalRHubPublisher 实现接口
3. SystemStateService 新增 Silent vs AndBroadcast 双路径方法，内嵌 BroadcastAsync 双通道发后即忘推送
4. UpdateMqttConnectionState 增强幂等检查 + 恢复双通道广播/断开仅SignalR + 快照补偿
5. GrpcServiceImpl 分流调用方：命令响应→Silent，异常/断连→AndBroadcast，删除手动推送约40行
6. CniLaser 删除 PublishLaserStateChangedAsync 死代码及注释MQTT推送，移除 _hubPublisher/_mqttEventPublisher 依赖
7. Program.cs DI 注册 IMqttEventPublisher/ISignalRHubPublisher 接口转发，Lazy<T> 打破循环依赖
8. 删除 [Obsolete] 过渡方法 UpdateCollectorState/UpdateLaserState/ResetCollectorState
9. 新增 19 个 TDD 单元测试（Silent 6 + Broadcast 5 + MQTT 连接 3 + 接口 2 + 常量 3）

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
```

---

## 四、本次提交详情

### 基本信息

| 字段 | 内容 |
|------|------|
| **提交时间** | 2026-05-10 18:30:00 |
| **作者** | NB11000 |
| **提交哈希** | `<will-be-generated>` |
| **基于提交** | `ca6d0bd` — `feat: LiDAR反演ArrayPool内存优化、模拟测试架构ADR与设备状态监控分析` (2026-05-10 17:30) |
| **变更统计（核心 27 文件）** | 27 files changed, +1565 insertions(+), -510 deletions(-) |

### 核心变更文件清单

| 状态 | 文件路径 | 变更说明 |
|------|----------|----------|
| 新建 | `WebAPI/Models/StateChangeEvents.cs` | 16 个事件类型字符串常量（+26 行） |
| 新建 | `WebAPI/Service/IMqttEventPublisher.cs` | MQTT 事件发布接口（+8 行） |
| 新建 | `WebAPI/Service/ISignalRHubPublisher.cs` | SignalR 推送接口（+8 行） |
| 修改 | `WebAPI/Service/SystemStateService.cs` | 核心深模块：新增 Silent/AndBroadcast/BroadcastAsync 方法，删除 [Obsolete] 过渡方法（+104/-XX 行） |
| 修改 | `WebAPI/Service/GrpcServiceImpl.cs` | 分流调用方，删除手动推送，清理死字段（+XX/-143 行） |
| 修改 | `WebAPI/Service/CniLaser.cs` | 删除死代码和已注释 MQTT 推送，移除 Publisher 依赖（+XX/-36 行） |
| 修改 | `WebAPI/Service/MqttEventPublisher.cs` | 实现 IMqttEventPublisher 接口（+1/-1 行） |
| 修改 | `WebAPI/Service/SignalRHubPublisher.cs` | 实现 ISignalRHubPublisher 接口（+1/-1 行） |
| 修改 | `WebAPI/Program.cs` | DI 接口转发注册（+2 行） |
| 新建 | `WebAPI.Tests/StateChangeEventsTests.cs` | 常量值验证测试（+38 行） |
| 新建 | `WebAPI.Tests/PublisherInterfaceTests.cs` | 接口实现验证测试（+19 行） |
| 新建 | `WebAPI.Tests/SystemStateServiceSilentTests.cs` | 静默更新 6 个测试（+151 行） |
| 新建 | `WebAPI.Tests/SystemStateServiceBroadcastTests.cs` | 广播更新 5 个测试（+168 行） |
| 新建 | `WebAPI.Tests/SystemStateServiceMqttConnectionTests.cs` | MQTT 连接状态 3 个测试（+96 行） |
| 修改 | `WebAPI.Tests/AcquisitionLifecycleCoordinatorTests.cs` | 方法名迁移 UpdateCollectorState→Silent（+8/-8 行） |
| 新建 | `.scratch/system-state-service-deep-module/PRD.md` | PRD 文档（+109 行） |
| 新建 | `.scratch/system-state-service-deep-module/IMPLEMENTATION_PLAN.md` | 实施计划（+238 行） |
| 新建 | `.scratch/system-state-service-deep-module/issues/01-08/*.md` | 8 个 TDD Issue（+454 行） |
| 修改 | `系统优化文档/深化重构方案1-SystemStateService深度事件发布模块.md` | 更新设计方案（+XX/-XX 行） |
| 删除 | `系统优化文档/深化重构方案2-告警到状态变更桥梁.md` | 方案已合并（-322 行） |

---

## 五、架构影响

```
变更前:
  调用方 (GrpcServiceImpl / CniLaser)
    ├── _stateService.UpdateCollectorState(...)  ← 仅更新缓存
    ├── _mqttEventPublisher.PublishStateChangedAsync(...)  ← 手动 MQTT 推送
    └── _hubPublisher.PublishStateChangedAsync(...)         ← 手动 SignalR 推送

变更后:
  调用方 (GrpcServiceImpl / CniLaser)
    └── _stateService.UpdateCollectorStateSilent(...)          ← 路径 [A]: 仅缓存
    └── _stateService.UpdateCollectorStateAndBroadcast(...)    ← 路径 [B]: 缓存+双通道广播
            └── BroadcastAsync()  ← 内部自动 MQTT + SignalR
```

| 维度 | 变更前 | 变更后 |
|------|--------|--------|
| 事件推送 | 调用方手写，散落各处 | SystemStateService 内部自动完成 |
| 路径语义 | 无区分，调用方自行判断 | Silent vs AndBroadcast 显式分流 |
| 推送失败 | 可能破坏调用方状态更新 | try/catch 隔离，不影响状态 |
| CniLaser 依赖 | 持有 SignalR/MQTT Publisher | 仅持有 IServiceProvider |
| GrpcServiceImpl 依赖 | 持有 6 个注入项 | 持有 4 个注入项 |
| 循环依赖 | 存在潜在风险 | Lazy<T> 打破 |
| 可测试性 | 依赖具体类 | 接口 Mock 支持纯单元测试 |

---

## 六、审核报告

> 审查范围：`SystemStateService.cs`、`GrpcServiceImpl.cs`、`CniLaser.cs`、`Program.cs` 及全部测试文件

### 通过项

| # | 检查点 | 详情 |
|---|--------|------|
| 1 | 路径 A/B 分流正确性 | 命令响应→Silent，异常/断连→AndBroadcast，MQTT 连接恢复→BroadcastAsync，断开→仅 SignalR |
| 2 | 事件流无重复触发 | EXIT→Silent 重置后 stream 断开→ResetCollectorStateAndBroadcast 幂等，AcquiringStateChanged 不重复触发 |
| 3 | 空安全 | `_mqttEventPublisher?.Value` 和 `_signalRHubPublisher?.` 覆盖单参数构造 null 场景 |
| 4 | 推送失败隔离 | BroadcastAsync 每通道独立 try/catch，失败不破坏状态更新 |
| 5 | DI 解析 | `Lazy<IMqttEventPublisher>` 由 .NET DI 原生支持，接口转发注册正确 |
| 6 | 测试覆盖 | 19 个新测试覆盖 Silent/Broadcast/MQTT 连接/接口/常量，118 次全部通过 |

### 已修复问题

| # | 严重度 | 位置 | 问题描述 | 修复 |
|---|--------|------|----------|------|
| 1 | 中 | `CniLaser.cs:399-413` | `PublishLaserStateChangedAsync` 死代码无调用点 | 删除方法 |
| 2 | 低 | `CniLaser.cs:89,99,117` | 3 处已注释 MQTT 推送代码 | 删除注释代码 |
| 3 | 低 | `GrpcServiceImpl.cs:168-172` | 已注释的 `_hubPublisher.PublishStateChangedAsync` | 删除注释代码块 |

### 遗留建议（非阻塞）

| # | 严重度 | 位置 | 建议 |
|---|--------|------|------|
| 1 | 低 | `GrpcServiceImpl.cs:18` | `using static System.Runtime.InteropServices.JavaScript.JSType;` 为预存无效 using（非本次引入） |
| 2 | 低 | `SystemStateService.cs:66-72` | 单参数构造函数仅测试使用，后续可考虑移除并统一测试构造方式 |

---

## 七、后续步骤预览（不在本次范围）

- 将 `UpdateLaserStateAndBroadcast` 接入 CniLaser 的硬件异常检测（如串口意外断开检测）
- MQTT RPC 激光器命令链路更多场景接入 Silent 路径
- 统一 `SystemStateService` 单参数构造函数的使用，仅保留三参数版本
