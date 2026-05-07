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

[实现计划](../IMPLEMENTATION_PLAN.md) — Slice 3

## What to build

在 LidarInverter 中实现 Cn² 闪烁方差法反演。

1. 内部维护环形缓冲区：`double[][] _ch1History` 和 `double[][] _ch2History`，容量 N=100（来自 LidarAlgorithmConfig.Cn2WindowFrames）
2. 每帧调用 Invert() 时：
   - 将当前帧校准后电压（预处理后的结果）写入环形缓冲区
   - 帧计数器 +1
   - 若 `frameCount < N` → `cn2Profile` 全填 -1.0（窗口不满）
   - 若 `frameCount >= N` → 对每个距离门 r_i：
     - 取 100 帧时间序列：`ch1_{1..N}[r_i], ch2_{1..N}[r_i]`
     - 计算归一化闪烁方差 `σI²`
     - `Cn2[r_i] = K × σI² × D^(7/3) × L^(-11/6)`（球面波形式）
     - K、D、L 均从 LidarAlgorithmConfig 读取
3. 此 Slice 的 Vis 部分沿用 Slice 2 已实现逻辑
4. 单通道模式下 Cn² 全填 -1.0（无需环形缓冲区操作）

## Acceptance criteria

- [ ] 单通道模式 Cn² 全为 -1.0
- [ ] 双通道模式前 99 帧 Cn² 全为 -1.0
- [ ] 双通道模式第 100 帧 Cn² 开始输出正值（> 0）
- [ ] 第 101 帧 Cn² 基于最近 100 帧滑动窗口更新（非静态值）
- [ ] Cn² 数组长度 = 帧采样点数（逐距离门输出）
- [ ] K 常数、D、L 参数从配置文件正确读取
- [ ] 无 GC 分配：环形缓冲区复用已有数组，不逐帧新建
- [ ] 测试包含：`Cn2_SingleChannel_ReturnsNegativeOne`、`Cn2_WindowNotFull_ReturnsNegativeOne`、`Cn2_Frame100_ReturnsValidValues`、`Cn2_SlidingWindow_UpdatesCorrectly`

## Blocked by

- [02-fernald-vis-inverter](02-fernald-vis-inverter.md)
