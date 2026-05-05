# TimeCalibration：tick → UTC DateTime 转换工具 + 测试

- **Category**: enhancement
- **State**: done
- **Blocked by**: None

## What to build

纯函数 `TimeHelper.ToUtcDateTime(sampleTick, referenceTick, referenceUtcTicks, frequency)`，将 `StructuredSample.Time`（Stopwatch tick）还原为 UTC `DateTime`。跨平台一致。

## Acceptance criteria

- [x] `ToUtcDateTime(referenceTick, referenceTick, refUtc, freq)` = reference time
- [x] `ToUtcDateTime(referenceTick + freq, referenceTick, refUtc, freq)` = refUtc + 1 秒
- [x] 负数 tick 差正确处理
- [x] 不同 Stopwatch.Frequency 下行为一致（Windows 10MHz / Linux 1GHz）
- [x] 24 小时大跨度无溢出

## Result

**实现**: `ConsoleApp1/Helpers/TimeHelper.cs`（14 行，纯函数）  
**测试**: `WebAPI.Tests/TimeHelperTests.cs`（5 tests, 5/5 passing）  
**测试项目**: `WebAPI.Tests/WebAPI.Tests.csproj`（新建，xunit, net8.0）
