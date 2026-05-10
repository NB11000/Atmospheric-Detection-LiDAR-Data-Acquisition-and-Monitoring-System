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

[实现计划](../IMPLEMENTATION_PLAN.md) — Slice 4

## What to build

增强 `UpdateMqttConnectionState(bool isConnected)` 方法，在原有"更新内部状态 + 触发 MqttConnectionStateChanged"基础上，新增路径 [B] 自动广播能力。

**行为规范：**

| 场景 | 触发条件 | 行为 |
|------|---------|------|
| 值不变 | `_mqttConnected == isConnected` | 直接返回，无任何操作（幂等，不变） |
| MQTT 断开 | false→false 不变 / true→false | `MqttConnectionStateChanged.Invoke(false)` + `_ = _signalRHubPublisher.PublishStateChangedAsync("mqtt_disconnected", "system", "MQTT 连接已断开", ...)` — MQTT 通道不可用，仅 SignalR |
| MQTT 恢复 | false→true | `MqttConnectionStateChanged.Invoke(true)` + `_ = _mqttEventPublisher.Value.PublishStateChangedAsync("mqtt_connected", "system", "MQTT 连接已恢复", ...)` + `_ = _signalRHubPublisher.PublishStateChangedAsync("mqtt_connected", "system", "MQTT 连接已恢复", ...)` — 双通道 + State 快照补偿 |

**快照补偿：**
- `mqtt_connected` 事件的 State 字段内嵌 `GetSystemState()` 完整快照
- 远端收到后可直接从 State 字段校准全部设备状态，无需额外请求
- MqttEventPublisher.PublishStateChangedAsync 已内置 `_stateService.GetSystemState()` 调用（无需改动），确保快照自动嵌入

**测试（TDD）：**
1. `UpdateMqttConnectionState_SameValue_NoOp` — true→true 或 false→false，无事件无推送
2. `UpdateMqttConnectionState_True_FiresEventAndPublishesBoth` — false→true 时 MqttConnectionStateChanged(true) + MQTT+SignalR 均推送 mqtt_connected
3. `UpdateMqttConnectionState_False_FiresEventAndPublishesSignalROnly` — true→false 时 MqttConnectionStateChanged(false) + SignalR 推送 mqtt_disconnected + MQTT 无调用

## Acceptance criteria

- [ ] 值不变时幂等跳过，无任何副作用
- [ ] MQTT 断开时：MqttConnectionStateChanged(false) 触发 + SignalR 推送 mqtt_disconnected + MQTT 无调用
- [ ] MQTT 恢复时：MqttConnectionStateChanged(true) 触发 + MQTT+SignalR 双通道推送 mqtt_connected + State 快照内嵌
- [ ] 推送为 fire-and-forget
- [ ] 现有 2 个 SystemStateServiceTests 测试用例（UpdateMqttConnectionState 内部事件）仍然通过
- [ ] 3 个新测试用例全部通过

## Blocked by

- [03-broadcast-methods](03-broadcast-methods.md)
