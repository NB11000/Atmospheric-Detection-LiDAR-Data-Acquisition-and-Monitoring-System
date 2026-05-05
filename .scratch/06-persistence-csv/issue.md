# 持久化线程：CoreDataBus → CSV

- **Category**: enhancement
- **State**: done
- **Blocked by**: #1, #2, `IAcquisitionBoundService`, `AcquisitionLifecycleCoordinator`

## What to build

在 WebAPI 进程内新增纯 Singleton 服务 `PersistenceService`，实现 `IAcquisitionBoundService` 接口（`RequiresMqttConnection = false`）。内部循环由 `AcquisitionLifecycleCoordinator` 通过 `Start()` / `Stop()` 驱动，按配置周期（1s / 5s / 30s / 1min / 5min）从 CoreDataBus 调用 `TryReadLatestSingle()`，将结构化采样点写入 CSV 文件。

- 不继承 `BackgroundService`，自行管理 CTS 和 `IDisposable`
- `Start()` / `Stop()` 线程安全幂等（`lock` + `_isRunning` 双重检查），与 `WaveformPublishService` 一致
- 通过 `TimeHelper.ToUtcDateTime()` 还原 UTC 绝对时间
- 按小时分片生成新文件（`{date}_HH.csv`）
- 批量写入 + 异步刷盘（`FileStream` + `StreamWriter` + `FlushAsync`）
- 文件头行：`Timestamp,UTC,CH1,CH2,Vis,Cn2,Temp,Humi,Press,WindSpd,Rain,WindDir`

## Acceptance criteria

- [x] 实现 `IAcquisitionBoundService`，`RequiresMqttConnection = false`
- [x] `Program.cs` 注册为 `AddSingleton<PersistenceService>()` + `AddSingleton<IAcquisitionBoundService>(sp => sp.GetRequiredService<PersistenceService>())`
- [x] 采集开始后自动启动循环，采集停止后自动停止
- [x] `Start()` / `Stop()` 线程安全幂等
- [x] 按配置周期（可配置）写入 CSV
- [x] UTC 时间列正确（通过 TimeHelper 还原）
- [x] 每小时自动切换新文件
- [x] 异常（磁盘满等）不导致进程崩溃
- [x] 优雅退出时刷盘最后一批
