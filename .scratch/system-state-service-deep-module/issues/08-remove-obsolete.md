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

[实现计划](../IMPLEMENTATION_PLAN.md) — Slice 8

## What to build

移除 SystemStateService 中在 Slice 2 标记为 `[Obsolete]` 的过渡方法，完成接口清理。

**删除项：**
1. `UpdateCollectorState(Func<CollectorStateDto, CollectorStateDto> updater)` — 已被 `UpdateCollectorStateSilent` 和 `UpdateCollectorStateAndBroadcast` 替代
2. `UpdateLaserState(Func<LaserStateDto, LaserStateDto> updater)` — 已被 `UpdateLaserStateSilent` 和 `UpdateLaserStateAndBroadcast` 替代
3. `ResetCollectorState()` — 已被 `ResetCollectorStateAndBroadcast` 替代

**前提条件：**
- Slice 5/6 已将所有调用方迁移至新方法
- 以上三个方法在代码库中无任何调用引用（搜索确认）

**测试（TDD）：**
不新增测试——删除过渡方法后，Slice 2/3/4 中编写的测试仍应通过（它们直接测试 Silent/Broadcast 方法，不依赖旧方法）。运行全量测试确认无编译错误和测试失败。

## Acceptance criteria

- [ ] `UpdateCollectorState` 方法已删除
- [ ] `UpdateLaserState` 方法已删除
- [ ] `ResetCollectorState` 方法已删除
- [ ] 编译通过，无引用旧方法的代码
- [ ] 全量单元测试通过（Silent/Broadcast 测试 + GrpcServiceImpl 测试 + 旧有测试）

## Blocked by

- [07-di-and-constructor-cleanup](07-di-and-constructor-cleanup.md)
