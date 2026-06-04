# Issue 1: SharedModels 配置 DTO 迁移

**Status:** `needs-triage`

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

[implementation-plan.md](./implementation-plan.md)

## What to build

将 `CaptureCardConfig`、`RadarConfig`、`LidarAlgorithmConfig`、`PersistenceSettings` 四个配置类从 `WebAPI` 项目移至 `SharedModels` 项目，更新所有引用路径和 using 语句，确保 WebAPI 项目和 ConfigLauncher 项目编译通过。

## Acceptance criteria

- [ ] 四个配置类文件物理位置在 `SharedModels/` 目录下，namespace 为 `SharedModels`
- [ ] WebAPI 项目通过 `ProjectReference` 引用 SharedModels，原有 `using WebAPI.Models` 改为 `using SharedModels`
- [ ] ConfigLauncher 项目可引用 SharedModels 中的配置类型（已有 ProjectReference）
- [ ] 整解编译通过（ConfigLauncher + WebAPI + SharedModels）
- [ ] 现有测试全部通过

## Blocked by

None - can start immediately
