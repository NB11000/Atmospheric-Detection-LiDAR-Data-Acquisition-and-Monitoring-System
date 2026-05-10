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

[实现计划](../IMPLEMENTATION_PLAN.md) — Slice 2

## What to build

在 SystemStateService 中新增路径 [A] 的 Silent 方法，并编写单元测试验证核心契约。

**新增方法：**
- `UpdateCollectorStateSilent(Func<CollectorStateDto, CollectorStateDto> updater)` — 更新采集卡缓存，当 Acquiring 值变化时触发 AcquiringStateChanged 内部事件，不广播
- `UpdateLaserStateSilent(Func<LaserStateDto, LaserStateDto> updater)` — 更新激光器缓存，不广播

**实现方式：**
- 提取现有 `UpdateCollectorState` / `UpdateLaserState` 的内部核心逻辑到 Silent 方法
- 现有 `UpdateCollectorState` / `UpdateLaserState` 标记 `[Obsolete]`，内部委托给 Silent 方法
- `ResetCollectorState` 同样标记 `[Obsolete]`，委托给 Silent reset 逻辑

**构造函数：**
- 新增参数 `Lazy<IMqttEventPublisher>` 和 `ISignalRHubPublisher`（本 Slice 中仅存储字段，不调用）

**测试（TDD）：**
1. `UpdateCollectorStateSilent_UpdatesCache` — 状态缓存正确更新
2. `UpdateCollectorStateSilent_DoesNotPublishToMqtt` — Mock IMqttEventPublisher 未被调用
3. `UpdateCollectorStateSilent_DoesNotPublishToSignalR` — Mock ISignalRHubPublisher 未被调用
4. `UpdateCollectorStateSilent_AcquiringChanged_FiresInternalEvent` — Acquiring 变化时 AcquiringStateChanged 触发
5. `UpdateLaserStateSilent_UpdatesCache_DoesNotPublish` — 激光器缓存更新 + 无发布调用

## Acceptance criteria

- [ ] `UpdateCollectorStateSilent` 方法存在且更新 volatile 缓存
- [ ] `UpdateLaserStateSilent` 方法存在且更新 volatile 缓存
- [ ] Acquiring 值变化时触发 AcquiringStateChanged 内部事件
- [ ] 两个 Silent 方法均不调用 IMqttEventPublisher / ISignalRHubPublisher
- [ ] 原有 UpdateCollectorState / UpdateLaserState / ResetCollectorState 标记 [Obsolete] 且行为不变（委托给 Silent）
- [ ] 5 个单元测试全部通过

## Blocked by

- [01-interfaces-and-constants](01-interfaces-and-constants.md)
