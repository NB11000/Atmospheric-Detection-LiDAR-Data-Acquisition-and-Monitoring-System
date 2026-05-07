# MqttRpcBackgroundService 解耦 WaveformPublishService

- **Category**: enhancement
- **State**: done
- **Blocked by**: #14

## What to build

移除 `MqttRpcBackgroundService` 对 `WaveformPublishService` 的直接依赖，改为通过 `SystemStateService` 更新 MQTT 连接状态，由 Coordinator 间接驱动波形发布。

- 构造函数注入 `SystemStateService`，移除 `WaveformPublishService` 注入
- `ConnectAsync` 末尾：`_waveformPublishService.Start()` → `_systemStateService.UpdateMqttConnectionState(true)`
- `OnDisconnectedAsync` 内：`_waveformPublishService.Stop()` → `_systemStateService.UpdateMqttConnectionState(false)`
- `StopAsync` 内：必要时设置 MQTT 状态为 false
- 移除字段 `_waveformPublishService`

## Acceptance criteria

- [ ] 编译通过
- [ ] MQTT 断连/重连时 Coordinator 正确响应（间接验证波形启停）
- [ ] RPC 路由功能不退化
- [ ] `MqttRpcBackgroundService` 不再引用 `WaveformPublishService` 类型
