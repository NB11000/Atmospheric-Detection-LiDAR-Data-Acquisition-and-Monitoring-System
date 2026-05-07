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

[实现计划](../IMPLEMENTATION_PLAN.md) — Slice 4

## What to build

将三步预处理集成到 LidarInverter.Invert() 的内部流水线中，在 Vis/Cn² 反演之前执行。

1. **暗电流扣除**：
   - 从每帧末尾取 `DarkCurrentSampleCount` 个点求电压均值作为暗电流
   - 整帧 `V[i] -= V_dark_ch1`（CH1）和 `V[i] -= V_dark_ch2`（CH2）
   - 若 `DarkCurrentSampleCount` 超出帧范围，用全帧均值

2. **距离平方校正**：
   - 距离计算 `r[i] = (i + r_offset) × c / (2 × fs)`
   - `V[i] = V[i] × r[i]²`
   - r_offset（距离零位偏置）从 LidarAlgorithmConfig 读取（默认 0）
   - 近场盲区 `r[i] < r_overlap` 的点置为 0（盲区补 0，不参与后续计算）

3. **双通道增益均衡**：
   - 仅在双通道模式（chSel=3）下执行
   - `V_ch2[i] *= GainEqualizationCoefficient`
   - 系数从 LidarAlgorithmConfig 读取

预处理在 Vis 和 Cn² 计算之前、同一帧内完成，为两者的输入做准备。

## Acceptance criteria

- [ ] 暗电流扣除：合成电压（已知信号 + 已知暗电流）扣除后误差 < 1%
- [ ] 距离平方校正：`V[i]` 与 `r[i]²` 成比例（验证远端点校正后的数值）
- [ ] 近场盲区：`r[i] < r_overlap` 的点电压为 0
- [ ] 双通道增益均衡后 CH1/CH2 均值比 ≈ 1.0
- [ ] 单通道模式不执行增益均衡（不报错）
- [ ] 所有预处理使用 `Span<T>` 避免堆分配
- [ ] 测试包含：`DarkCurrent_SubtractsCorrectly`、`RangeCorrection_AppliesCorrectly`、`GainEqualization_BalancesChannels`、`NearFieldOverlap_MaskedToZero`

## Blocked by

- [03-cn2-scintillation-inverter](03-cn2-scintillation-inverter.md)
