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

[实现计划](../IMPLEMENTATION_PLAN.md) — Slice 5

## What to build

将 LidarInverter 嵌入 AD_Controlcs.Analysis 线程。

1. AD_Controlcs 构造函数新增 `LidarAlgorithmConfig` 参数，创建 `_lidarInverter = new LidarInverter(config)`
2. 在 Analysis 方法的 `for (int i = 0; i < count; i++)` 循环之前调用：
   ```csharp
   var (vis, cn2Profile) = _lidarInverter.Invert(voltageBlock, chSel);
   ```
3. 循环内将：
   - `detArr[i].Vis = 0.0` → `detArr[i].Vis = vis`
   - `detArr[i].Cn2 = 0.0` → `detArr[i].Cn2 = cn2Profile[i]`
4. Invert 调用包裹 try-catch：异常帧 Vis = -1.0, Cn2 全 -1.0，记录日志但不中断线程循环
5. Cn2Profile 数组在 Invert() 内部由 ArrayPool 分配，Analysis 线程逐点消费后不负责归还（内部管理生命周期）

## Acceptance criteria

- [ ] Analysis 线程正常消费电压块并调用 LidarInverter
- [ ] StructuredSample 中 Vis 字段不再为 0.0（单通道/双通道均验证）
- [ ] StructuredSample 中 Cn2 字段在双通道模式下第 100 帧起不为 0.0
- [ ] 单通道模式下 Cn2 为 -1.0
- [ ] LidarInverter 异常不导致 Analysis 线程崩溃
- [ ] CoreDataBus 写入不受影响（Vis/Cn2 填充后正常写入）
- [ ] DetectionChannel 整批写入不受影响
- [ ] 端到端验证：启动采集 → 持久化 CSV 文件中 Vis 列不为 0

## Blocked by

- [04-preprocessing-pipeline](04-preprocessing-pipeline.md)
