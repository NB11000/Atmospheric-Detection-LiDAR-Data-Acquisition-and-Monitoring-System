# 采集生命周期协调器未实例化导致四个绑定服务无法启动

- **Label**: needs-triage
- **Blocked by**: None

## Problem Statement

在真实 MQTT 模式下，前端通过 MQTT RPC 发送 `collector-start-ad` 指令后，设备端 RPC 返回成功（`AD_STARTED`），采集状态缓存 `Acquiring` 被正确置为 `true`。但波形发布、低频发布、检测告警发布、持久化写入四个服务均未启动，导致前端图表无数据显示、CSV 文件无新增记录。

**根因**：`AcquisitionLifecycleCoordinator` 在 DI 容器中注册了单例（`AddSingleton`），但没有任何类通过构造函数注入该类型。.NET DI 容器采用懒加载策略，只有在首次被 `GetRequiredService<>()` 解析时才会实例化。由于无人引用，该单例的构造函数从未执行，其对 `SystemStateService.AcquiringStateChanged` 和 `SystemStateService.MqttConnectionStateChanged` 的事件订阅从未发生。

当 RPC 成功后 `UpdateCollectorStateSilent` 触发 `AcquiringStateChanged?.Invoke(true)` 时，`?` 空条件运算符因为事件无订阅者而静默跳过，导致 `Apply()` 方法从未被调用，所有 `IAcquisitionBoundService` 实现的 `Start()` 方法从未被执行。

## Solution

两处修改：

1. 在 `Program.cs` 应用启动完成回调中强制解析 `AcquisitionLifecycleCoordinator`，触发 DI 容器立即实例化该单例。
2. 在 `AcquisitionLifecycleCoordinator` 构造函数中从 `SystemStateService.GetSystemState()` 读取当前 MQTT 连接状态初始化 `_mqttConnected` 字段，防止晚创建时错过早已触发的 `MqttConnectionStateChanged` 事件。

## User Stories

1. 作为一个数据采集系统操作员，当我通过 MQTT RPC 发送 `collector-start-ad` 指令后，波形数据发布服务应自动启动，使前端波形图实时显示采集数据。
2. 作为一个数据采集系统操作员，当我开始采集后，低频环境数据（Cn²、Vis、温湿度、气压等）应每 7 秒通过 MQTT 发布一次，供前端图表呈现。
3. 作为一个数据分析人员，当我开始采集后，数据持久化服务应每 5 秒将最新低频数据样本追加写入当天的 CSV 文件。
4. 作为一个系统管理员，当我停止采集后，所有绑定服务（波形发布、低频发布、检测告警、持久化）应立即停止，避免无效资源消耗。
5. 作为一个系统管理员，当 MQTT 连接在采集过程中断开时应暂停需要 MQTT 连接的绑定服务，连接恢复后立即恢复发布。
6. 作为一个开发人员，当我重启设备端 WebAPI 进程后，`AcquisitionLifecycleCoordinator` 应正确读取当前 MQTT 连接状态，不会因为晚实例化而遗漏已建立的连接。
7. 作为一个开发人员，当采购卡子进程通过 gRPC 连接后，采集生命周期协调器应已就绪并等待采集状态变更事件。

## Implementation Decisions

- **修改模块**：`AcquisitionLifecycleCoordinator`（采集生命周期协调器）和 `Program.cs`（应用入口）。
- **实例化策略**：在 `ApplicationStarted` 回调中通过 `GetRequiredService<AcquisitionLifecycleCoordinator>()` 强制解析，与其他共享内存/核心数据总线初始化放在同一生命周期阶段。
- **初始状态同步**：构造函数中调用 `systemStateService.GetSystemState().Server.IsMqttConnected` 拉取当前 MQTT 连接状态，因为 `AcquisitionLifecycleCoordinator` 实例化时机晚于 MQTT 首次连接，已触发的 `MqttConnectionStateChanged` 事件无法被新订阅者捕获。
- **受影响服务**：`WaveformPublishService`、`LowFrequencyPublisher`、`DetectionPublisherService`、`PersistenceService` — 全部实现 `IAcquisitionBoundService` 接口，由 `AcquisitionLifecycleCoordinator` 统一管理 Start/Stop 生命周期。
- **RequriresMqttConnection 处理**：已经正确处理 — `WaveformPublishService`、`LowFrequencyPublisher`、`DetectionPublisherService` 设置为 `true`，采集停止或 MQTT 断连时暂停；`PersistenceService` 设置为 `false`，仅受采集状态控制。

## Testing Decisions

- **测试范围**：`AcquisitionLifecycleCoordinator` 的事件订阅、启停逻辑和初始状态同步。
- **已有测试**：`Test/WebAPI.Tests/AcquisitionLifecycleCoordinatorTests.cs` 已涵盖 `OnAcquiringStateChanged`、`OnMqttConnectionStateChanged`、`Apply()` 的逻辑测试，包括 `RequiresMqttConnection` 条件分支。现有测试已通过构造函数直接实例化 `AcquisitionLifecycleCoordinator`，绕过 DI 容器。
- **新增验证**：重启设备端服务后使用 MQTT 客户端工具（如 MQTTX）订阅 `daq/{machineId}/waveform/ch1` 主题，发送 `collector-start-ad` RPC，确认波形数据开始在 Broker 上出现。
- **回归验证**：确认 `collector-stop-ad` 或子进程 gRPC 断开时，四个服务正确停止。

## Out of Scope

- 前端代码修改（前端 MQTT 链路已验证完整正常）
- 子进程 `command_response.Content` 精确字符串格式的修改
- `SystemStateService` 双重构造函数的简化
- 其他未注入的 DI 单例排查

## Further Notes

- 此 bug 从连接池重构以来一直存在。Mock 模式不受影响，因为 mock 模式下波形/低频生成器由前端 `useMockGenerators` Hook 直接注入消息，不依赖设备端 `AcquisitionLifecycleCoordinator`。
- 修复后，设备端启动日志中应出现「采集生命周期协调器已启动，等待采集状态变更事件」。
- 点击"开始采集"后，应立即看到「采集状态变更: 采集中」「波形发布循环已启动」「低频发布服务已启动」「持久化服务已启动」「检测告警服务已启动」等信息级日志。
