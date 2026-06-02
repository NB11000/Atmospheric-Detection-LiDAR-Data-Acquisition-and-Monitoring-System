# 02 — ConnectAsync 接入 PublishDeviceOnlineAsync

- **Label**: needs-triage
- **Parent**: [IMPLEMENTATION_PLAN.md](../IMPLEMENTATION_PLAN.md)
- **Blocked by**: 01-will-payload-and-publisher

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

在 `MqttRpcBackgroundService.ConnectAsync` 中，于 `SubscribeAsync` 完成后、`UpdateMqttConnectionState(true)` 之前，调用 `await _mqttEventPublisher.PublishDeviceOnlineAsync()`。

每次 CONNACK 成功（含重连）都发 retained online，覆盖 Broker 上可能残留的 Will offline。方法内部抛异常不单独 catch——由 `ConnectAsync` 调用方（`ExecuteAsync` 或 `OnDisconnectedAsync` 重连循环）统一处理。

## Acceptance criteria

- [ ] `ConnectAsync` 中 `SubscribeAsync` 之后有 `await _mqttEventPublisher.PublishDeviceOnlineAsync()` 调用
- [ ] 调用在 `_systemStateService.UpdateMqttConnectionState(true)` 之前（retained online 先到达订阅方，再推 state_changed 内部事件）
- [ ] 首次连接成功 → Broker `events/will` retained 为 online
- [ ] 重连成功 → Broker `events/will` retained 覆盖为 online（覆盖可能的 Will offline）
- [ ] `PublishDeviceOnlineAsync` 抛异常时，`ConnectAsync` 不吞异常，向上传播
