# 04 — SystemStateService 去掉 mqtt_connected 的 MQTT 广播

- **Label**: needs-triage
- **Parent**: [IMPLEMENTATION_PLAN.md](../IMPLEMENTATION_PLAN.md)
- **Blocked by**: 02-connect-async-online-publish

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

## What to build

`SystemStateService.UpdateMqttConnectionState(true)` 路径中，移除 `BroadcastAsync("mqtt_connected", ...)` 调用。保留：
- `MqttConnectionStateChanged?.Invoke(true)` — 采集生命周期协调器依赖
- SignalR 推送 — 兼容现有 WebSocket 前端

`UpdateMqttConnectionState(false)` 路径不改（本就只推 SignalR）。

## Acceptance criteria

- [ ] `mqtt_connected` 事件不再发布到 MQTT `state_changed` topic
- [ ] `MqttConnectionStateChanged?.Invoke(true)` 仍然触发
- [ ] SignalR 推送 `mqtt_connected` 仍然执行
- [ ] `mqtt_disconnected` 路径行为不变（对照测试）
