# AcquisitionLifecycleCoordinator 实现

- **Category**: enhancement
- **State**: done
- **Blocked by**: #12, #14

## What to build

新建 `AcquisitionLifecycleCoordinator`（纯 Singleton），统一管理所有 `IAcquisitionBoundService` 的生命周期。

- 通过 `IEnumerable<IAcquisitionBoundService>` 发现所有服务
- 订阅 `SystemStateService.AcquiringStateChanged` 和 `SystemStateService.MqttConnectionStateChanged`
- 维护内部 `_acquiring` / `_mqttConnected` 状态
- 任一事件触发时，遍历所有服务计算 `CanRun = Acquiring && (!RequiresMqtt || MqttConnected)`，`true` 调 `Start()`，`false` 调 `Stop()`
- 自身不加锁（各服务保证幂等）
- 不继承 `BackgroundService`

## Acceptance criteria

- [ ] `Program.cs` 注册为 `AddSingleton`
- [ ] 采集开始 → 所有 `RequiresMqtt=false` 的服务启动
- [ ] 采集开始 + MQTT 已连接 → 所有服务启动
- [ ] MQTT 断连 → `RequiresMqtt=true` 的服务停止，其余继续
- [ ] 采集停止 → 所有服务停止
- [ ] 重复 Start/Stop 不报错（服务端幂等保护）
