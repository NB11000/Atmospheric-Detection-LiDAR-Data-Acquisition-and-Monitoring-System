# 低频 UI MQTT 发布线程

- **Category**: enhancement
- **State**: done
- **Blocked by**: #1, #2, `IAcquisitionBoundService`, `AcquisitionLifecycleCoordinator`

## What to build

在 WebAPI 进程内新增纯 Singleton 服务 `LowFrequencyPublisher`，实现 `IAcquisitionBoundService` 接口（`RequiresMqttConnection = true`）。内部循环由 `AcquisitionLifecycleCoordinator` 通过 `Start()` / `Stop()` 驱动，每 7 秒从 CoreDataBus 调用 `TryReadLatestSingle()`，还原 UTC 时间后发布至 MQTT 低频 Topic。

- 不继承 `BackgroundService`，自行管理 CTS 和 `IDisposable`
- `Start()` / `Stop()` 线程安全幂等（`lock` + `_isRunning` 双重检查），与 `WaveformPublishService` 一致
- Topic: `daq/{MachineId}/lowfreq`
- QoS: 1（至少一次，保障可靠交付）
- Payload: JSON（含全部 12 字段 + 还原后的 UTC 时间字符串）
- 复用现有 `MqttEventPublisher` DI 实例，不创建新 MQTT 连接
- 读取失败（总线无数据）时跳过本周期，不发布
- MQTT 断连时 Coordinator 自动调 `Stop()`，重连且采集中自动调 `Start()`

## Acceptance criteria

- [x] 实现 `IAcquisitionBoundService`，`RequiresMqttConnection = true`
- [x] `Program.cs` 注册为 `AddSingleton<LowFrequencyPublisher>()` + `AddSingleton<IAcquisitionBoundService>(sp => sp.GetRequiredService<LowFrequencyPublisher>())`
- [x] 采集开始 + MQTT 已连接时自动启动，任一条件不满足时自动停止
- [x] `Start()` / `Stop()` 线程安全幂等
- [x] 每 7s 发布一条（无数据时跳过）
- [x] Topic 格式：`daq/{MachineId}/lowfreq`
- [x] QoS 1，Retain=否
- [x] Payload 含还原后的 UTC 时间
- [x] 不阻塞其他后台服务
