# MQTT 双连接架构与采集生命周期修复

- **Label**: needs-triage
- **Blocked by**: None

## Problem Statement

在真实 MQTT 模式下，系统存在三个互相关联的缺陷：

1. **采集启动后波形/低频/持久化/检测服务未启动**：`AcquisitionLifecycleCoordinator` 注册了 DI 单例但从未被注入引用，懒加载导致从未实例化，事件订阅链断裂。

2. **MQTT 采集期间连接掉线后不自动重连**：MQTTnet 的 keepalive PING 超时触发断连，原因码为 `NormalDisconnection`。重连逻辑中对该原因码做了早期返回，误将其等同于用户主动断连，导致掉线后永久离线。

3. **采集期间 QoS 0 波形洪流阻塞 QoS 1 控制通道**：波形以 100ms 间隔发布 2×8KB 数据，与 RPC 响应、低频数据、状态变更等 QoS 1 消息共享同一条 MQTT 连接。MQTTnet 内部对出站报文串行化，QoS 0 波形填满 TCP 发送缓冲区后，QoS 1 消息被无限期排队，表现为 RPC 超时（10s）和低频数据刷新间隔逐渐拉长至一分钟以上。

## Solution

三处修复协同解决：

1. **启动时强制实例化 `AcquisitionLifecycleCoordinator`**，使其订阅 `SystemStateService` 的 `AcquiringStateChanged` 和 `MqttConnectionStateChanged` 事件，驱动四个 `IAcquisitionBoundService` 的启停。同时构造函数中从当前状态快照同步初始 MQTT 连接状态，防止晚创建时错过已触发的连接事件。

2. **删除 `OnDisconnectedAsync` 中对 `NormalDisconnection` 的早期返回**。服务正常关闭时早已通过 `_shouldReconnect = false` 在 `StopAsync` 中阻止重连，该检查冗余且误杀 keepalive 超时场景。

3. **为波形数据建立独立的 MQTT 连接**，与控制/低频通道物理隔离。波形连接只发不收（无订阅、无遗嘱），QoS 0 洪流不再挤占主连接的 TCP 发送缓冲区，RPC 响应和低频数据始终畅通。

## User Stories

1. 作为一个数据采集系统操作员，当我通过 MQTT RPC 发送 `collector-start-ad` 指令后，波形数据应立即开始发布到 MQTT Broker，前端图表可实时显示。
2. 作为一个数据采集系统操作员，当采集启动后，低频环境数据（Cn²、Vis、六要素）应每 7 秒正常通过 MQTT 发布，前端图表按时刷新。
3. 作为一个数据分析人员，当采集启动后，数据持久化服务应每 5 秒将低频数据追加写入 CSV 文件。
4. 作为一个系统管理员，当我发送 `collector-stop-ad` 指令时，RPC 应在 10 秒内返回成功响应，不受波形流量影响。
5. 作为一个系统管理员，当采集过程中 MQTT 连接因 keepalive 超时而断开时，两条连接均应自动重连，无需人工干预。
6. 作为一个系统管理员，当主连接正常恢复后，RPC 通道、状态变更推送应立即恢复可用。
7. 作为一个系统管理员，当波形连接正常恢复后，波形数据应立即恢复发布。
8. 作为一个系统管理员，当我停止采集后，所有绑定服务（波形、低频、检测、持久化）应立即停止，释放系统资源。
9. 作为一个系统管理员，当服务正常关闭时，离线遗嘱消息应正常发布到 Broker，前端接收到 `device_offline` 事件。
10. 作为一个系统管理员，当服务进程崩溃时，Broker 应自动发布 `process_crashed` 遗嘱消息。

## Implementation Decisions

### 修改模块

| 模块 | 职责 |
|------|------|
| `AcquisitionLifecycleCoordinator` | 采集生命周期协调器：订阅采集状态和 MQTT 连接状态事件，统一管理绑定服务启停 |
| `MqttRpcBackgroundService` | MQTT 后台服务：管理主连接和波形连接的生命周期（创建、连接、重连、关闭） |
| `MqttEventPublisher` | MQTT 事件发布器：暴露两个客户端属性，波形发布使用专用连接 |
| `WaveformPublishService` | 波形发布服务：移除对波形发布的 `await`，定时循环不再受 TCP 缓冲区阻塞影响 |
| `Program.cs` | 应用入口：启动时强制实例化 `AcquisitionLifecycleCoordinator` |

### 接口变更

- `MqttEventPublisher` 新增 `WaveformMqttClient` 属性（`IMqttClient?`），供波形发布专用连接
- `WaveformPublishService` 的 `PublishWaveformDataAsync` 方法改为 `void` 返回类型（Fire-and-forget）

### 架构决策

- **双连接隔离**：波形走 `ClientId = {MachineId}-waveform`，主连接走 `ClientId = {MachineId}`。Broker 需要支持至少 2 个并发连接（EMQX Serverless 免费版提供 2/1,000，满足需求）。
- **波形连接特性**：纯发送通道——不订阅主题、不设遗嘱消息、不注册 `ApplicationMessageReceived` 处理器。`CleanSession = true`（无需恢复会话状态）。
- **波形连接重连**：简单固定间隔（3s）重连循环，不设上限。波形数据允许丢失，不需要指数退避。
- **低频与检测告警**：QoS 1 消息保留在主连接，享受遗嘱和状态上下文。
- **初始状态同步**：`AcquisitionLifecycleCoordinator` 构造时通过 `systemStateService.GetSystemState().Server.IsMqttConnected` 拉取当前连接状态，确保晚创建时不会因为 `_mqttConnected` 默认为 `false` 导致 `Apply()` 短路。

## Testing Decisions

### 测试原则

- 专注于外部行为验证（服务启停、事件触发、连接隔离），不测试内部实现细节。
- 现有测试框架：`Test/WebAPI.Tests/AcquisitionLifecycleCoordinatorTests.cs` 已覆盖事件订阅和 `Apply()` 逻辑。

### 测试范围

1. **`AcquisitionLifecycleCoordinator`**：验证 `OnAcquiringStateChanged(true)` 触发后所有 `IAcquisitionBoundService.Start()` 被调用；验证 `OnMqttConnectionStateChanged(false)` 后 `RequiresMqttConnection = true` 的服务被 `Stop()` 而持久化服务不受影响。
2. **双连接隔离**：验证波形连接断连不影响主连接的 RPC 和低频发布；验证主连接断连时波形发布不受影响（`RequiresMqttConnection` 条件正确）。
3. **重连验证**：模拟 keepalive 超时断连，确认主连接自动重连并恢复 RPC 订阅。

### 验证方式

- 重启设备端服务后使用 MQTT 客户端工具（如 MQTTX）订阅 `daq/{machineId}/waveform/ch1`，发送 `collector-start-ad` RPC，确认波形数据出现。
- 采集期间发送 `collector-stop-ad`，确认 10 秒内返回成功。
- 采集期间主动断开网络 30 秒后恢复，确认两条连接均自动重连。
- 启动设备时观察日志，确认「采集生命周期协调器已启动」和「波形连接已建立」两条日志出现。

## Out of Scope

- 前端代码修改（前端 MQTT 链路已验证完整正常）
- `SystemStateService` 双重构造函数的合并简化
- MQTT 配置选项（Broker 地址、端口、凭证）的变更
- 其他可能存在的"注册但未注入"的 DI 单例排查
- 将 `ConnectAsync` 中重复的 TLS/auth 代码抽取为公共方法

## Further Notes

- 此修复的前两个 bug 从连接池重构以来一直存在，mock 模式不受影响（mock 模式下波形/低频由前端 `useMockGenerators` Hook 直接注入消息）。
- 双连接架构下，重启后设备日志应出现「波形连接已建立」和「采集生命周期协调器已启动」。
- 如果未来需要将 `LowFrequencyPublisher` 也移到波形连接上（例如数据量增大时），只需修改其使用的客户端引用即可，架构已预留扩展空间。
