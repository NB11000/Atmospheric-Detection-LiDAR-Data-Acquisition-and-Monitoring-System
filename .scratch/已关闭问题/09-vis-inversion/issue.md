# Vis 反演接入 Analysis

- **Category**: enhancement
- **State**: ready-for-human
- **Blocked by**: #3

## What to build

实现 `VisibilityCalculator` 纯逻辑模块，在 Analysis 线程 for 循环内调用，计算结果写入 `StructuredSample.Vis`，替换当前占位 0.0。

- 输入：CH1/CH2 电压信号（同帧 `StructuredSample[]` 的滑动窗口）
- 算法：基于背景扣除后的距离校正信号，通过消光系数反演能见度
- 参考：`开发记录/大气检测激光雷达数据解析链路 — 实施计划.md` 中的 Vis 反演步骤
- 性能约束：单点计算耗时不能使 for 循环整体超帧间隔（~50ms / 92160 = ~0.5μs/点 → 需批量处理或跳点计算）

## Acceptance criteria

- [ ] `VisibilityCalculator.Calculate()` 纯函数，可独立测试
- [ ] Analysis 循环内占位 0.0 被真实值替换
- [ ] 异常输入（全零信号、负值）不崩溃
- [ ] 单帧总处理时间 ≤ 50ms（不拖慢采集）
