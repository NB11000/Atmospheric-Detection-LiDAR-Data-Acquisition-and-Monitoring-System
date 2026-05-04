# CoreDataBus 总线行为测试组件

- **Label**: done
- **Blocked by**: #1

## What to build

为 `CoreDataBus` 的单进程内行为编写集成测试，使用真实 `MemoryMappedFile.CreateNew`（非 mock），覆盖：

- `Create()` 头初始化：`WriteIndex=0`、`ChannelCount/BufferLength/SampleRate` 正确、`ReferenceTick` + `ReferenceUtcTicks` 非零
- `Write()` → `TryReadLatestSingle()` 往返：写入一条后读到同一条
- 多次写入后 `TryReadLatestSingle()` 读到最新一条（非第一条）
- `WriteIndex == 0` 时 `TryReadLatestSingle()` 返回 `false`
- 写入 `BufferLength + 1` 条后取模正确（环形覆盖）
- `Open()` 后 `header` 指针指向正确映射地址

## Acceptance criteria

- [x] 单条写入 → TryRead 读到相同 Timestamp/Time/CH1/CH2
- [x] 写入 N 条 → TryRead 读到第 N-1 条（最新）
- [x] 空总线 TryRead 返回 false，sample 为 default
- [x] BufferLength 满后写入不崩溃，取模正确
- [x] Create 后 ReferenceTick > 0 且 ReferenceUtcTicks > 0

## Result

**接口变更**: `CoreDataBus` 构造函数支持可选 `mapName` 参数（默认 `"DAQ_CORE_DATA_BUS"`），新增 6 个公共只读属性暴露头字段。

**测试**: `WebAPI.Tests/CoreDataBusTests.cs`（6 tests, 6/6 passing）
