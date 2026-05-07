# 检测线程完整逻辑

- **Category**: enhancement
- **State**: ready-for-human
- **Blocked by**: #4

## What to build

将检测线程从占位代码升级为完整的多条件检测流水线。

**SignalQualityDetector**（纯逻辑模块，可独立测试）：
- 信号遮挡：CH1 或 CH2 连续 N 点 < ±0.01（可配置阈值）
- 噪声检测：滑动窗口标准差超过阈值
- 波形畸变：峰值/谷值异常偏离历史基线
- 遍历帧内**全部**采样点（不再 break-first），收集所有检测结果

**ConditionAssessor**（纯逻辑模块，可独立测试）：
- 综合 Vis/Cn² 阈值判定工况等级（正常/注意/异常）
- 告警分级：一般告警 / 严重告警
- 阈值可配置文件调整

输出：`IReadOnlyList<DetectionResult>` → 检测线程逐条发送告警 → gRPC → WebAPI → MQTT。

## Acceptance criteria

- [ ] 全部采样点被检测（不遗漏百帧内后续异常）
- [ ] 遮挡/噪声/畸变三项检测各自独立，互不短路
- [ ] 告警分级正确：严重（如信号全零 >1s）vs 一般（如单点跳变）
- [ ] 阈值通过配置文件可调
- [ ] SignalQualityDetector 纯函数，可无 Channel 测试
