---
Status: done
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

实现 LidarInverter 的 Fernald (Klett) 能见度反演算法。此 Slice 只做 Vis，Cn² 留到 Slice 3。

1. 新建 `ConsoleApp1/Service/LidarInverter.cs`，暴露 `Invert(Voltage_block, byte chSel) → (double vis, double[] cn2Profile)`
2. 此 Slice 只实现 Vis 分支：`cn2Profile` 全填 -1.0（哨兵值）
3. Fernald 算法流程：
   - 距离平方校正：`S[i] = V[i] × r[i]²`（`r[i] = i × c / (2 × fs)`）
   - `ln(S[i])` 对数化
   - 滑动窗口线性回归求斜率 → 消光系数 `α[i] = -slope / 2`
   - 后向积分（从远端边界点向近端）：`α_a(r) = ...` Fernald 积分递推式
   - 远端边界条件：`α_a(r_max)` 假设为洁净大气（从 `ln S` 远端斜率取均值）
   - `Vis = 3.912 / α_eff`（取有效路径消光系数的中值或远端代表值）
4. 支持三种 chSel 模式：单 CH1 / 单 CH2 / 双通道（CH1+CH2 各算 Vis 取均值）
5. 边界区域（近场盲区）跳过，不参与 Vis 计算

## Acceptance criteria

- [ ] `LidarInverter.Invert()` 在单通道模式下返回有效 Vis 值（> 0）
- [ ] 均匀消光合成数据：Vis 与理论值偏差 < 5%
- [ ] 单 CH2 模式也返回有效 Vis
- [ ] 双通道模式 Vis 为 CH1/CH2 各自 Vis 的均值
- [ ] `cn2Profile` 全为 -1.0（哨兵值，此 Slice 不做 Cn²）
- [ ] 无 GC 分配：内部使用 `Span<T>` + `stackalloc`
- [ ] 测试文件 `WebAPI.Tests/Lidar/LidarInverterTests.cs` 包含 `Fernald_UniformAtmosphere_ReturnsCorrectVis` 和 `Fernald_Ch2Only_UsesChannel2`

## Blocked by

- [01-lidar-algorithm-config](01-lidar-algorithm-config.md)
