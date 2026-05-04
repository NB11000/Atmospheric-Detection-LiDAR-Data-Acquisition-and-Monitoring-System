# SystemStateService 新增 MqttConnectionStateChanged 事件

- **Label**: ready-for-agent
- **Blocked by**: —

## What to build

在 `SystemStateService` 上新增 MQTT 连接状态追踪，与现有 `AcquiringStateChanged` 对称。

- 新增 `event Action<bool>? MqttConnectionStateChanged`
- 新增 `UpdateMqttConnectionState(bool isConnected)` 方法，仅在值变化时触发事件
- 内部用 `volatile bool` 缓存当前状态
- 跟踪 MQTT 连接状态，供 Coordinator 订阅

## Acceptance criteria

- [ ] 事件仅在值实际变化时触发（幂等，不会连续两次 `true` 触发两次）
- [ ] `GetSystemState()` 等现有方法不受影响
- [ ] Coordinator 可订阅并收到正确的 bool 值
