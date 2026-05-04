# Analysis 结构化填充 + 时间戳测试

- **Label**: done
- **Blocked by**: #2

## What to build

为 Analysis 线程的 `StructuredSample` 填充逻辑编写测试，验证从 `Voltage_block` 到结构化数据的转换正确性：

- 帧内 `Time` 严格单调递增：`sample[i].Time = baseTick + i * ticksPerSample`
- `Timestamp` 跨帧连续递增：`globalSampleIndex` 不因新帧而重置
- `Time` 帧间严格单调：前一帧最后一条的 Time < 后一帧第一条的 Time
- CH1/CH2 从 `Voltage_block.Voltage1[i]` / `Voltage2[i]` 正确取值
- Vis/Cn² 占位为 0.0
- 气象扩展字段（Temp/Humi/Press/WindSpd/Rain/WindDir）占位为 0.0

将填充逻辑提取为可独立测试的纯函数，减少对真实 Channel 的依赖。

## Acceptance criteria

- [x] 帧内 Time 严格递增（i → i+1 差值 = ticksPerSample）
- [x] Timestamp 第二帧从第一帧的 count 处继续（非 0）
- [x] 帧间 Time 不重叠（前帧最后一 tick < 后帧第一 tick）
- [x] CH1[i] = Voltage_block.Voltage1[i]，CH2 同理
- [x] null 通道（单通道模式）时对应字段 = 0
- [x] Vis/Cn²/气象字段 = 0.0

## Result

**提取**: `ConsoleApp1/Helpers/SampleFiller.cs` — 两个静态方法：
- `ComputeTicksPerSample(long frequency, int sampleRateKhz)` → 纯函数
- `FillOne(ref StructuredSample, int indexInFrame, long baseTick, long ticksPerSample, ref long globalSampleIndex, double[]? voltage1, double[]? voltage2)` → 填充全部 12 字段

**修改**: `ConsoleApp1/Service/AD_Controlcs.cs` — Analysis 循环内联赋值替换为 `SampleFiller.FillOne(...)` 调用

**测试**: `WebAPI.Tests/SampleFillerTests.cs`（6 tests, 6/6 passing）
