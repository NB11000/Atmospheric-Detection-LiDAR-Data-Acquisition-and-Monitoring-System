---
Status: needs-triage
Created: 2026-05-10
---

# PRD: SystemStateService 深模块重构——状态监控与事件发布

## Problem Statement

系统的前后端交互遵循"强乐观 UI + 命令链路正确性"架构。状态变更存在两条路径：

- **路径 [A] — MQTT RPC 命令链路**：命令响应已携带确认，调用方乐观更新 UI，不需要 events/state_changed 广播。
- **路径 [B] — 命令链路之外的异常变更**：设备异常断开、硬件故障、检测告警等，无命令响应作为确认载体，必须主动广播。

当前 SystemStateService 的接口不区分这两种路径。它的 7 个公共成员（UpdateCollectorState / UpdateLaserState / ResetCollectorState / UpdateMqttConnectionState + 3 个读取方法）只管理状态值，不管理变更通知。路径 [B] 的广播决策被推到 4 个不同调用方身上，导致：

- MQTT 推送 5/9 场景覆盖，4 种完全缺失（激光器全部 MQTT 推送被注释，MQTT 断连/恢复无广播，检测告警与 state_changed 割裂）
- MQTT 和 SignalR 覆盖不对称（GrpcServiceImpl 连接时推 MQTT 不推 SignalR，CniLaser 推 SignalR 不推 MQTT）
- 新增路径 [B] 场景时开发者必须记住手动添加推送代码，容易遗漏

## Solution

将 SystemStateService 从"只管理状态值"的浅模块深化为区分两条更新路径的深模块（deep module）。核心思路：

- **路径 [A] — Silent 方法**：仅更新缓存，不广播。供 MQTT RPC Handler 在命令成功后调用。
- **路径 [B] — Broadcast 方法**：更新缓存 + 自动双通道广播（MQTT + SignalR）。供异常事件调用方使用。
- **调用方只需选择路径**：不必知道 MqttEventPublisher 和 SignalRHubPublisher 的存在。

同时补齐 MQTT 断连/恢复的对外广播能力：MQTT 断开时通过 SignalR 推送 mqtt_disconnected（MQTT 通道不可用，Will Message broker 侧兜底）；MQTT 恢复时通过 MQTT + SignalR 双通道推送 mqtt_connected + 完整 SystemStateDto 快照补偿。

## User Stories

1. As a 远程监控系统（订阅 state_changed 的客户端）, I want 在采集子进程 gRPC 连接/断开时收到 collector_connected/collector_disconnected 事件, so that 我可以感知采集进程在线状态而无需主动轮询
2. As a 远程监控系统, I want 在设备异常断开时收到 device_disconnected（而非通用 Error）事件, so that 我可以触发特定告警并提示运维人员检查 USB 硬件
3. As a 远程监控系统, I want 在采集异常终止时收到 acquisition_failed 事件, so that 我可以区分"正常停止"和"异常终止"
4. As a 远程监控系统, I want 在设备打开失败时收到 device_open_failed 事件, so that 我可以快速定位故障原因
5. As a 远程监控系统, I want 在 MQTT 恢复时收到带有完整 SystemStateDto 快照的 mqtt_connected 事件, so that MQTT 断连期间的状态变化在恢复后一次性校准
6. As a 前端 UI 客户端, I want 在 MQTT 断连时通过 SignalR 收到 mqtt_disconnected, so that 即使 MQTT 通道中断仍能感知离线状态并禁用手动操作按钮
7. As a 前端 UI 客户端, I want 在 MQTT 恢复时收到 mqtt_connected + 快照, so that 我可以立即恢复到最新 UI 状态无需手动刷新
8. As a 后端开发者, I want 调用方只需选择 Silent 或 AndBroadcast 方法, so that 新增路径 [B] 场景不会遗漏推送逻辑
9. As a 后端开发者, I want GrpcServiceImpl 和 CniLaser 不再直接持有 MqttEventPublisher / SignalRHubPublisher, so that 这些模块的测试不需要 mock 发布器
10. As a 测试编写者, I want 可通过 mock 发布器接口验证 Silent 不触发发布而 AndBroadcast 必然触发, so that 路径区分的关键契约有自动化测试保障

## Implementation Decisions

### 核心设计

- **方法拆分而非枚举参数**：方法名本身就是文档——Silent 不需要 eventType/reason 参数，避免无效参数污染
- **SystemStateService 直接持有两个 Publisher**：不引入中间事件层。两个 Publisher 是其唯一需要的下游，Lazy<T> 已解决循环依赖
- **双通道推送均 fire-and-forget（`_ =`）**：路径 [B] 推送是"尽力而为"通知。MQTT 有 QoS 1 保证投递，SignalR 是降级兼容通道。不阻塞状态更新
- **Lazy<T> 打破循环依赖**：SystemStateService → IMqttEventPublisher，MqttEventPublisher → SystemStateService。Lazy<T> 语义清晰，.NET DI 原生支持
- **错误事件使用具体 eventType**：每个 error code 对应独立 StateChangeEvents 常量，远端无需解析 reason 字符串
- **提取接口支持 TDD**：IMqttEventPublisher / ISignalRHubPublisher（仅 PublishStateChangedAsync），SystemStateService 依赖接口

### 模块变更

| 模块 | 变更 |
|------|------|
| SystemStateService | 新增 UpdateCollectorStateSilent / UpdateLaserStateSilent / UpdateCollectorStateAndBroadcast / UpdateLaserStateAndBroadcast / ResetCollectorStateAndBroadcast；注入 Lazy<IMqttEventPublisher> + ISignalRHubPublisher；增强 UpdateMqttConnectionState（断开→SignalR，恢复→MQTT+SignalR+快照补偿） |
| StateChangeEvents | 新增常量类：13 个事件类型字符串常量 |
| IMqttEventPublisher | 新增接口：PublishStateChangedAsync |
| ISignalRHubPublisher | 新增接口：PublishStateChangedAsync |
| GrpcServiceImpl | 命令响应→Silent；gRPC 连接/断开/错误→AndBroadcast；删除手动 MQTT/SignalR 推送代码；UpdateStateFromError 内部改用 AndBroadcast 并按 error code 传具体 eventType |
| CniLaser | 删除 PublishLaserStateChangedAsync（死代码）+ 被注释的 MQTT 调用；UpdateLaserStateCache 改用 UpdateLaserStateSilent；移除 SignalRHubPublisher / MqttEventPublisher 依赖 |
| Program.cs | SystemStateService DI 注册新增 Lazy<IMqttEventPublisher> + ISignalRHubPublisher |

### 不变更的文件

- **CollectorHandler / LaserHandler**：不直接调状态更新方法（仅读取），无改动
- **MqttRpcBackgroundService**：已调 UpdateMqttConnectionState，广播逻辑内置于方法中
- **AcquisitionLifecycleCoordinator**：仍订阅 AcquiringStateChanged / MqttConnectionStateChanged 内部事件，接口不变
- **MqttEventPublisher / SignalRHubPublisher**：实现逻辑不变，仅加接口

## Testing Decisions

### 测试原则

- 仅测试外部行为契约，不耦合实现细节
- Silent 契约：状态缓存更新 + 无发布器调用
- AndBroadcast 契约：状态缓存更新 + 双通道发布器各被调用一次且参数正确
- UpdateMqttConnectionState 契约：值不变=无操作；值变化=内部事件触发+对应通道广播

### 测试范围

| 模块 | 测试类型 | 验证点 |
|------|---------|--------|
| SystemStateService | 单元测试（mock IMqttEventPublisher / ISignalRHubPublisher） | Silent 不触发发布；AndBroadcast 必触发双通道；UpdateMqttConnectionState 断开/恢复/幂等；状态缓存正确更新 |
| GrpcServiceImpl | 单元测试 | 命令响应路径调 Silent；错误路径按 error code 调 AndBroadcast 且 eventType 具体；不再直接调用 Publisher |

### 现有测试遗产

- SystemStateServiceTests（2 个用例，覆盖 UpdateMqttConnectionState 内部事件）——扩展为覆盖 Silent/Broadcast 路径
- WebAPI.Tests 使用 xUnit + NullLogger 模式

## Out of Scope

- 激光器硬件意外断开的主动检测机制（CniLaser 无串口监控线程）
- DetectionPublisherService 告警与 state_changed 的集成（告警仍走 detection/alerts 主题）
- [STATE] 结构化上报机制的实现
- 子进程僵死/无响应的主动健康评估（PING/PONG 周期性探测）
- CniLaser 的 DI 注册方式改造（Service Locator → 构造函数注入）
- 前端代码适配

## Further Notes

- MQTT 断开时仅走 SignalR，MQTT 推送自动跳过（IsConnected 检查）；Will Message broker 侧兜底
- MQTT 恢复快照补偿：GetSystemState() 内嵌 mqtt_connected 事件的 State 字段，远端一次性校准全部设备状态
- StateChangeEvents 常量类使用 const string，编译期检查消除拼写错误
- 过渡方案：Phase 1 保留旧方法标记 [Obsolete] → Phase 2 迁移调用方 → Phase 3 删除旧方法和调用方对 Publisher 的直接依赖
