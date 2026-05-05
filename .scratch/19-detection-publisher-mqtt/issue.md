# DetectionPublisherService — 检测告警 MQTT 发布服务

- **Category**: enhancement
- **State**: todo
- **Blocked by**: #18

## Parent

从 `开发计划/检测告警链路实现计划.md` 拆解，Issue 2/2。

## What to build

WebAPI 侧新增 `DetectionPublisherService`，实现 `IAcquisitionBoundService`，接收来自 gRPC 的结构化 `DetectionAlert` 并发布到专属 MQTT topic `daq/{machineId}/detection/alerts`。

**切过的完整路径**：`GrpcServiceImpl → DetectionPublisherService → MQTT daq/{id}/detection/alerts`

具体改动：
- **新建** `WebAPI/Service/DetectionPublisherService.cs`：
  - 实现 `IAcquisitionBoundService`（`RequiresMqttConnection = true`）
  - `lock` + `_isRunning` 双重检查保证 `Start()`/`Stop()` 幂等
  - 内部 `ConcurrentQueue<DetectionAlert>` + `AutoResetEvent` 缓冲池
  - 后台 `Task` 循环消费队列 → `MqttEventPublisher` 发布 JSON
  - `OnAlertReceived(DetectionAlert alert)` 由 `GrpcServiceImpl` 调用
  - Payload JSON 含 alarmType, severity, timestamp, time, ch1, ch2, utcTime
  - QoS 1，不 Retain
- `WebAPI/Service/GrpcServiceImpl.cs`：
  - 构造函数新增 `DetectionPublisherService` 参数
  - `"Detection"` message_type 分支：`Any.Unpack<DetectionAlert>()` → `OnAlertReceived()`
- `WebAPI/Program.cs`：DI 双注册（`AddSingleton<DetectionPublisherService>()` + `AddSingleton<IAcquisitionBoundService>(...)`）

## Acceptance criteria

- [ ] `DetectionPublisherService` 实现 `IAcquisitionBoundService`，`RequiresMqttConnection = true`
- [ ] `Start()` / `Stop()` 线程安全幂等
- [ ] gRPC `"Detection"` 消息正确路由到 `OnAlertReceived()`
- [ ] Payload JSON 包含 7 个字段（alarmType, severity, timestamp, time, ch1, ch2, utcTime）
- [ ] MQTT topic 格式：`daq/{machineId}/detection/alerts`，QoS 1，不 Retain
- [ ] `dotnet build WebAPI` 零错误
- [ ] `dotnet test WebAPI.Tests` 全部通过，无退化

## Blocked by

- #18 — 需要 proto 中的 `DetectionAlert` 类型
