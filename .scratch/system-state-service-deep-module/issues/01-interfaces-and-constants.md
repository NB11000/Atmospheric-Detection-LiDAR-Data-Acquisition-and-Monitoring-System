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

[实现计划](../IMPLEMENTATION_PLAN.md) — Slice 1

## What to build

创建三个基础设施件，不改变任何现有行为：

1. **StateChangeEvents**：新建 `WebAPI/Models/StateChangeEvents.cs`，包含 16 个 const string 事件类型常量（collector_connected/disconnected、device_opened/closed/disconnected、acquisition_started/stopped/failed、device_open_failed、laser_connected/disconnected、laser_on/off、error、mqtt_connected/disconnected）
2. **IMqttEventPublisher**：新建 `WebAPI/Service/IMqttEventPublisher.cs`，提取 `PublishStateChangedAsync(string eventType, string source, string reason, string message)` 方法签名。MqttEventPublisher 实现该接口
3. **ISignalRHubPublisher**：新建 `WebAPI/Service/ISignalRHubPublisher.cs`，提取 `PublishStateChangedAsync(string eventType, string source, string reason, string message)` 方法签名。SignalRHubPublisher 实现该接口

三个文件的创建不涉及任何调用方改动——纯新建，可直接编译通过。

## Acceptance criteria

- [ ] `StateChangeEvents` 类包含全部 16 个 const string 常量
- [ ] `MqttEventPublisher` 显式实现 `IMqttEventPublisher` 接口
- [ ] `SignalRHubPublisher` 显式实现 `ISignalRHubPublisher` 接口
- [ ] 编译通过，无任何现有测试因接口引入而失败

## Blocked by

None - can start immediately
