---
Status: needs-triage
Parent: ../IMPLEMENTATION_PLAN.md
---

## Execution Rules

> **此 Issue 执行顺序不可变更，必须遵循 TDD 红绿重构循环：**
>
> **1. RED** — 先写一个测试，确认测试 FAIL。禁止一次写多个测试。
> **2. GREEN** — 写最少代码让当前测试 PASS。禁止预判未来测试。
> **3. REFACTOR** — 消除重复、深化模块。禁止 RED 期间重构。
>
> **硬禁止：**
> - 禁止"先全部实现再补测试"（水平切片反模式）
> - 禁止跳过 RED 直接写 GREEN
> - 测试必须通过公共接口验证行为，不耦合实现细节
> - 每次循环只一个测试 → 一个实现，垂直切片推进

## Parent

[实现计划](../IMPLEMENTATION_PLAN.md) — Slice 5

## What to build

将 GrpcServiceImpl 中的 SystemStateService 调用按路径分流，删除手动 MQTT/SignalR 推送代码。

**路径 [A] → Silent（不改动推送行为，仅换方法名）：**
- `UpdateStateFromCommandResponse` 中的所有 `UpdateCollectorState(...)` → `UpdateCollectorStateSilent(...)`。包括 OPEN_DEVICE、CLOSE_DEVICE、START_AD、STOP_AD、EXIT、COMMAND_HANDLE_FAILED 六个 case

**路径 [B] → AndBroadcast（原来手动推送 + 状态更新合并为一次调用）：**
- gRPC 连接（Communicate 第 124-148 行）：`UpdateCollectorState(...)` + `_ = _mqttEventPublisher.PublishStateChangedAsync(...)` → `UpdateCollectorStateAndBroadcast(..., StateChangeEvents.CollectorConnected, "采集子进程 gRPC 连接已建立")`
- gRPC 断开（PublishCollectorDisconnectedAsync 第 358-386 行）：`ResetCollectorState()` + MQTT push + SignalR push → `ResetCollectorStateAndBroadcast("采集子进程 gRPC 连接已断开")`
- DEVICE_DISCONNECTED（UpdateStateFromError 第 522-530 行）：`UpdateCollectorState(...)` → `UpdateCollectorStateAndBroadcast(..., StateChangeEvents.DeviceDisconnected, reason)`
- ACQUISITION_FAILED（第 533-542 行）：`UpdateCollectorState(...)` → `UpdateCollectorStateAndBroadcast(..., StateChangeEvents.AcquisitionFailed, reason)`
- DEVICE_OPEN_FAILED（第 545-554 行）：`UpdateCollectorState(...)` → `UpdateCollectorStateAndBroadcast(..., StateChangeEvents.DeviceOpenFailed, reason)`
- default error case（第 557-567 行）：保持 `UpdateCollectorStateSilent`（一般性错误不广播）

**删除手动推送代码：**
- Communicate 中 MessageType == "Error" 分支（第 196-207 行）：删除 `_ = _mqttEventPublisher.PublishStateChangedAsync(...)` 和 `await _hubPublisher.PublishStateChangedAsync(...)` 两段代码
- PublishCollectorDisconnectedAsync（第 358-386 行）：删除 MQTT + SignalR 推送代码（由 ResetCollectorStateAndBroadcast 内化）

**测试（TDD）：**
1. `UpdateStateFromCommandResponse_OpenDevice_CallsUpdateSilent` — OPEN_DEVICE 成功 → 调 UpdateCollectorStateSilent
2. `UpdateStateFromError_DeviceDisconnected_CallsUpdateAndBroadcast` — DEVICE_DISCONNECTED → 调 UpdateCollectorStateAndBroadcast(device_disconnected)
3. `UpdateStateFromError_AcquisitionFailed_CallsUpdateAndBroadcast` — ACQUISITION_FAILED → acquisition_failed
4. `UpdateStateFromError_DeviceOpenFailed_CallsUpdateAndBroadcast` — DEVICE_OPEN_FAILED → device_open_failed
5. `Connect_PublishesCollectorConnected` — 采集子进程连接 → UpdateCollectorStateAndBroadcast(collector_connected)
6. `Disconnect_PublishesCollectorDisconnected` — 采集子进程断开 → ResetCollectorStateAndBroadcast

**注意：** GrpcServiceImpl 测试需要一个 mock/fake 的 SystemStateService。由于 GrpcServiceImpl 直接依赖具体类 `SystemStateService`（非接口），测试策略是创建一个可注入的 TestDouble（继承或包装），或使用 Moq 对虚方法进行 mock（需要将 Silent/AndBroadcast 方法标记为 virtual）。

## Acceptance criteria

- [ ] 路径 [A] 所有命令响应 case 均调 UpdateCollectorStateSilent
- [ ] 路径 [B] DEVICE_DISCONNECTED/ACQUISITION_FAILED/DEVICE_OPEN_FAILED 均调 UpdateCollectorStateAndBroadcast 且 eventType 具体
- [ ] gRPC 连接 → UpdateCollectorStateAndBroadcast(collector_connected)
- [ ] gRPC 断开 → ResetCollectorStateAndBroadcast(collector_disconnected)
- [ ] Communicate 中 Error 分支不再有 _mqttEventPublisher / _hubPublisher 调用
- [ ] PublishCollectorDisconnectedAsync 中不再有 _mqttEventPublisher / _hubPublisher 调用
- [ ] GrpcServiceImpl 不再以任何方式直接调用 _mqttEventPublisher.PublishStateChangedAsync 或 _hubPublisher.PublishStateChangedAsync
- [ ] 6 个单元测试全部通过

## Blocked by

- [04-mqtt-connection-broadcast](04-mqtt-connection-broadcast.md)
