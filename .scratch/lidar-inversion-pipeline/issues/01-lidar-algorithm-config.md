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

创建 LidarAlgorithmConfig 配置模型并接入现有配置加载链路。

1. 新建 `ConsoleApp1/Models/LidarAlgorithmConfig.cs`：包含所有反演算法需要的配置参数（增益均衡系数、K 常数、接收孔径 D、路径长度 L、滑动窗口大小 N、Fernald 远端边界距离、激光波长、Angstrom 指数、暗电流采样点数）
2. 在 `DeviceConfig.json` 中新增 `LidarAlgorithm` 配置节，填入合理的默认值
3. 在 `ConfigHelper` 中新增 `LidarAlgorithmConfig` 的读取方法，与其他配置节读取模式保持一致
4. AD_Controlcs 构造函数中注入 LidarAlgorithmConfig

## Acceptance criteria

- [ ] `LidarAlgorithmConfig` 类包含所有必要字段，默认值正确（K=4.48, N=100, Angstrom=1.3）
- [ ] `DeviceConfig.json` 新增 `LidarAlgorithm` 节，所有字段可配置
- [ ] `ConfigHelper` 能正确读取并反序列化 `LidarAlgorithmConfig`
- [ ] AD_Controlcs 支持通过 DI 注入 `LidarAlgorithmConfig`（不改动现有 4 线程行为）

## Blocked by

None - can start immediately
