# 01 — Will payload 格式更新 + MqttEventPublisher 新增方法

- **Label**: needs-triage
- **Parent**: [IMPLEMENTATION_PLAN.md](../IMPLEMENTATION_PLAN.md)
- **Blocked by**: None — can start immediately

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

**MqttRpcBackgroundService 构造函数**：Will payload 从旧 3 字段格式改为新 6 字段统一格式。

**MqttEventPublisher**：新增两个无参方法 —— `PublishDeviceOnlineAsync()` 和 `PublishDeviceOfflineAsync()`。内部固定填充 6 字段 payload，向 `daq/{MachineId}/events/will` 发布 retain=true、QoS=1 的消息。

这是整个方案的基石——后续三个 slice 依赖这两个方法。

## Acceptance criteria

- [ ] `_willPayloadBytes` 构造的 JSON 包含 6 字段：`status`(`"offline"`)、`ts`(`0`)、`eventType`(`"process_crashed"`)、`source`(`"mqtt_broker"`)、`message`、`timestamp`(零值)
- [ ] `PublishDeviceOnlineAsync()` 发布到 topic `daq/{MachineId}/events/will`，retain=true，QoS=1
- [ ] `PublishDeviceOnlineAsync()` payload 中：`status="online"`, `eventType="device_online"`, `source="device"`, `ts`≈当前 ms, `timestamp`≈当前 UTC
- [ ] `PublishDeviceOfflineAsync()` payload 中：`status="offline"`, `eventType="device_offline"`, `source="device"`
- [ ] `MqttClient` 为 null 时两个方法不抛异常，仅记日志
- [ ] 单元测试覆盖：Will payload JSON 合法性 + 两个新方法的 topic / payload / retain / QoS
