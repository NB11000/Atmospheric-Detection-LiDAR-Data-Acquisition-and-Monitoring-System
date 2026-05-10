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

[实现计划](../IMPLEMENTATION_PLAN.md) — Slice 6

## What to build

清理 CniLaser 中的死代码和被注释的 MQTT 推送调用，将激光器状态更新迁移到 Silent 方法。

**删除项：**
1. `PublishLaserStateChangedAsync` 方法（第 399-413 行）— 死代码，无任何调用点
2. Connect 中被注释的 MQTT 推送（第 89 行）：`// _ = _mqttEventPublisher.PublishStateChangedAsync("laser_connected", ...)`
3. Connect catch 中被注释的 MQTT 推送（第 99 行）：`// _ = _mqttEventPublisher.PublishStateChangedAsync("laser_connection_error", ...)`
4. Disconnect 中被注释的 MQTT 推送（第 117 行）：`// _ = _mqttEventPublisher.PublishStateChangedAsync("laser_disconnected", ...)`

**迁移项：**
1. `UpdateLaserStateCache` 中 `stateService.UpdateLaserState(...)` → `stateService.UpdateLaserStateSilent(...)`（Path A：主动操作通过 MQTT RPC 触发，不需要广播）
2. Connect（第 86 行）/ Disconnect（第 115 行）/ LaserOn（第 264 行）/ LaserOff（第 281 行）→ 均通过 UpdateLaserStateCache → UpdateLaserStateSilent

**注意：** 本次不新增硬件断线主动检测。未来的 Path B laser_disconnected 广播将由硬件监控线程调用 UpdateLaserStateAndBroadcast 实现。

此 Slice 为纯重构——删除死代码、切换方法调用——不改变运行时行为。不新增测试（无新逻辑）。

## Acceptance criteria

- [ ] `PublishLaserStateChangedAsync` 方法已删除
- [ ] 4 处被注释的 `_mqttEventPublisher.PublishStateChangedAsync` 已删除
- [ ] `UpdateLaserStateCache` 改用 `UpdateLaserStateSilent`
- [ ] 编译通过，激光器控制功能正常（Connect/Disconnect/LaserOn/LaserOff 状态缓存仍正确更新）
- [ ] 无现有功能退化

## Blocked by

- [04-mqtt-connection-broadcast](04-mqtt-connection-broadcast.md)
