# CoreDataBus 时间戳时区修正与溢出修复

- **Label**: needs-triage
- **Blocked by**: None

## Problem Statement

设备端输出的低频数据（Mqtt 发布和 CSV 持久化）中的时间戳存在两个问题：

1. **时区错误**：`TimeHelper.ToUtcDateTime` 输出的是 UTC 零时区时间（`2026-06-05T16:20:49Z`），对于北京时间（UTC+8）的用户来说偏差 8 小时，前端图表横轴和 CSV 文件时间与实际观测时间不符。

2. **潜在溢出风险**：`ToUtcDateTime` 中的整数乘法 `elapsedTicks * 10_000_000L` 在系统连续运行约 25 小时后可能溢出 `long` 上限（9.2 × 10^18），导致时间戳出现错误的负值或回绕。

## Solution

三处修改协同解决：

1. **`TimeHelper.ToUtcDateTime`**：将整数乘法改为 double 中间量计算，消除溢出风险。
2. **`LowFrequencyPublisher`**：UTC 时间加 8 小时转换为北京时间，输出格式从 `Z`（零时区）改为 `+08:00`。
3. **`PersistenceService`**：同上，CSV 文件中时间戳也使用北京时间。

## User Stories

1. 作为一个数据采集系统操作员，当我在前端图表上查看 Cn²/Vis 数据时，横轴时间应显示北京时间（UTC+8），而非零时区时间。
2. 作为一个数据分析人员，当我打开 CSV 持久化文件时，每条记录的时间戳应是北京时间，可以直接与前端图表对应。
3. 作为一个系统管理员，当设备连续运行超过 25 小时后，时间戳计算结果不应出现溢出或异常值。

## Implementation Decisions

- **修改模块**：`TimeHelper`（时间转换工具）、`LowFrequencyPublisher`（MQTT 低频发布）、`PersistenceService`（CSV 持久化）。
- **时区转换策略**：设备端加 8 小时，前端无需修改。`.AddHours(8)` 是 `DateTime` 标准方法，不引入第三方依赖。
- **溢出修复方式**：`(long)((double)elapsedTicks * 10_000_000L / frequency)`。`double` 精度（53 位尾数）足以精确表示 ~10^15 范围内的整数，远小于现实场景中 `elapsedTicks` 的取值范围。
- **输出格式**：`yyyy-MM-ddTHH:mm:ss.fffffff+08:00`，明确标注东八区偏移，前端可直接解析。
- **结构体 `utc` 字段命名**：MQTT JSON 中的 `UTC` 字段名保持不变（避免前端破窗），但内容已转为北京时间。

## Testing Decisions

- **已有测试**：`TimeHelperTests.cs` — 验证 UTC 转换公式的正负方向和精度。
- **测试原则**：验证 `ToUtcDateTime` 的溢出修复对大值 `elapsedTicks` 的处理；验证 `.AddHours(8)` 结果比原始 UTC 多 8 小时。
- **数据验证**：部署后在 MqttX 中查看 `lowfreq` 消息的 `utc` 字段，应显示 `+08:00` 结尾的北京时间；CSV 文件中时间戳同理。

## Out of Scope

- 前端图表的时区显示逻辑（前端已在设备端修正后无需改动）
- 其他数据源（如 `state_changed` 事件的 `timestamp` 字段）的时区转换
- `TimeHelper` 的重构或抽取公共接口

## Further Notes

- `ToUtcDateTime` 虽然方法名保留 `Utc`，但调用方自行 `.AddHours(8)` 转换。如果未来需要支持多时区，可将时区偏移量提取为配置项。
- `ConsoleApp1` 和 `WebAPI` 项目各自引用 `TimeHelper`（通过共享项目引用），修改一次即全局生效。
