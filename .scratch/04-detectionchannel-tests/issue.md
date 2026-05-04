# DetectionChannel 数组所有权 + 线程生命周期测试

- **Label**: done
- **Blocked by**: #2

## What to build

为 `DetectionChannel<DetectionBatch>` 的数组所有权传递和线程生命周期编写测试，覆盖：

**数组所有权**：
- 正常路径：Analysis `TryWrite` → Detection 线程 `ReadAsync` → 消费后 `ArrayPool.Return`
- 背压路径：`DropOldest` 触发 `dropped` 回调归还被挤出数组
- 写失败路径：`TryWrite` 返回 `false` 时调用方自行归还
- 确认无二次归还（同一数组不会通过两条路径归还）

**生命周期**：
- `init()` 清空 Channel 残留数据并归还数组
- `stop()` 执行 `TryComplete` + 清空 + 归还 + 线程 `Join`

## Acceptance criteria

- [x] 数组正常传递路径：租用 → 写入 → 消费 → 归还（无泄漏）
- [x] Channel 满时旧元素被 dropped 回调正确归还
- [x] TryWrite 失败时调用方归还、dropped 不触发（互斥）
- [x] init() 清空后 Channel 无残留数据
- [x] stop() 后所有线程正常退出

## Result

**测试**: `WebAPI.Tests/DetectionChannelTests.cs`（4 tests, 4/4 passing）
- 测试中内联创建 `Channel<DetectionBatch>`，配置与 `AD_Controlcs.CreateNewDataChannel()` 一致
- 无生产代码修改
