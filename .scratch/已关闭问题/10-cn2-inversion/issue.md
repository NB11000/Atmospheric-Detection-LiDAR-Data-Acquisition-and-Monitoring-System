# Cn² 反演接入 Analysis

- **Category**: enhancement
- **State**: ready-for-human
- **Blocked by**: #3, #9

## What to build

实现 `Cn2ProfileInverter` 纯逻辑模块，在 Analysis 线程 for 循环内调用（于 Vis 计算之后），计算结果写入 `StructuredSample.Cn2`。

- 输入：同帧 CH1/CH2 信号 + Vis 估算值
- 算法：基于折射率结构常数廓线反演，输出 `double[]` 廓线值，`StructuredSample.Cn2` 存表面层代表值
- 参考：`开发记录/大气检测激光雷达数据解析链路 — 实施计划.md` 中的 Cn² 反演步骤
- 性能约束同 #9

## Acceptance criteria

- [ ] `Cn2ProfileInverter.Invert()` 纯函数，可独立测试
- [ ] Analysis 循环内占位 0.0 被真实值替换
- [ ] 异常输入不崩溃
- [ ] 单帧总处理时间（Vis + Cn² 合计）≤ 50ms
