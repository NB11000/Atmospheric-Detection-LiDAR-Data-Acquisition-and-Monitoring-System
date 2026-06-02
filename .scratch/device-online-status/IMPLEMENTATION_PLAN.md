# 设备在线状态监控：Will + Retained 实施计划

## Modules

| 模块 | 位置 | 单一职责 |
|------|------|---------|
| MqttEventPublisher | `WebAPI/Service/MqttEventPublisher.cs` | 新增两个 retained 发布方法，掩盖 payload 构造细节 |
| MqttRpcBackgroundService | `WebAPI/Service/MqttRpcBackgroundService.cs` | 管理 MQTT 连接生命周期中的在线/离线 publish 时机；Will payload 构造；shutdown 时的重连竞态保护 |
| SystemStateService | `WebAPI/Service/SystemStateService.cs` | 停止 `mqtt_connected` 的 MQTT 广播，SignalR + 内部事件保留 |
| MqttEventPublisherTests | `Test/` | 单元测试：验证新增方法的 topic / payload / retain / QoS |
| SimulationRunner 场景 | `SimulationRunner/` | 集成测试：end-to-end 崩溃→Will→重启→online 完整链路 |

## Interfaces

### MqttEventPublisher 新增方法

```
Task PublishDeviceOnlineAsync()
    → 发布到 daq/{MachineId}/events/will
    → retain: true, QoS: 1
    → payload: {"status":"online","ts":<当前ms>,"eventType":"device_online","source":"device","message":"设备已上线","timestamp":"<当前UTC>"}

Task PublishDeviceOfflineAsync()
    → 发布到 daq/{MachineId}/events/will
    → retain: true, QoS: 1
    → payload: {"status":"offline","ts":<当前ms>,"eventType":"device_offline","source":"device","message":"设备正常下线","timestamp":"<当前UTC>"}
```

两个方法均无参数。内部从 `_mqttSettings.CurrentValue.MachineId` 获取 MachineId 拼接 topic。如果 `MqttClient == null || !MqttClient.IsConnected`，记日志后直接 return（不抛异常，`ConnectAsync` 里的调用方有异常处理）。

### MqttRpcBackgroundService 改动

**构造函数**：`_willPayloadBytes` 改为新 6 字段格式。

**ConnectAsync**：`SubscribeAsync` 完成后，`UpdateMqttConnectionState(true)` 之前，调 `await _mqttEventPublisher.PublishDeviceOnlineAsync()`。该方法不处理返回值或异常——抛了由 `ConnectAsync` 的调用方捕获。

**StopAsync**：改造流程为：

```
_shutdownCts.Cancel()
_shouldReconnect = false

if (connected):
    try { PublishDeviceOfflineAsync() → success = true }
    catch { success = false }

UpdateMqttConnectionState(false)      // SignalR only + 内部事件
_reconnectLock.WaitAsync

if (connected && success):
    DisconnectAsync()
    Dispose client
else:
    // 不 Dispose，OS 关 TCP，Will 触发
```

**新增字段**：`private readonly CancellationTokenSource _shutdownCts = new()`。`ConnectAsync` 调用链使用 `CancellationTokenSource.CreateLinkedTokenSource(_shutdownCts.Token, callerToken).Token`。

### SystemStateService 改动

`UpdateMqttConnectionState(true)` 路径中，移除 `BroadcastAsync("mqtt_connected", ...)` 调用。`MqttConnectionStateChanged?.Invoke(true)` 和 SignalR 推送保留。

`UpdateMqttConnectionState(false)` 不改（本就只有 SignalR）。

## Data Flow

### Happy Path: 设备上线

```
服务启动 → ExecuteAsync → ConnectAsync
  → 构造连接选项（含新 Will payload）
  → await _mqttClient.ConnectAsync
  → await SubscribeAsync("$rpc/{MachineId}/#")
  → await _mqttEventPublisher.PublishDeviceOnlineAsync()
      → PublishAsync(topic, payload, QoS=1, retain=true)
      → Broker 存储 retained: {..., status:"online", ...}
  → _systemStateService.UpdateMqttConnectionState(true)
      → SignalR 推送 mqtt_connected
      → MqttConnectionStateChanged?.Invoke(true) → Coordinator 启动需 MQTT 的绑定服务
```

### Happy Path: 正常下线

```
服务停止 → StopAsync
  → _shutdownCts.Cancel()              // 终止可能的重连 ConnectAsync
  → _shouldReconnect = false
  → await PublishDeviceOfflineAsync()
      → PublishAsync(topic, payload, QoS=1, retain=true)
      → success = true
  → UpdateMqttConnectionState(false)
      → SignalR 推送 mqtt_disconnected
      → MqttConnectionStateChanged?.Invoke(false) → Coordinator 停止需 MQTT 的绑定服务
  → _reconnectLock.WaitAsync
  → 注销事件 → DisconnectAsync → Dispose → Broker retained: {..., status:"offline", ...}
```

### Error Path: publish offline 失败

```
StopAsync
  → _shutdownCts.Cancel()
  → PublishDeviceOfflineAsync() → 抛异常（网络已断）
  → success = false
  → UpdateMqttConnectionState(false)
  → 不调 DisconnectAsync / Dispose
  → 进程退出 → OS 关闭 TCP socket → Broker 检测 keepalive 超时
  → Broker 触发 Will → retained: {..., status:"offline", ...}
```

### Error Path: 崩溃

```
进程崩溃（未调 StopAsync）→ TCP 断开 → Broker keepalive 超时
  → Broker 发布 Will → retained: {..., status:"offline", eventType:"process_crashed", ...}
```

### Error Path: 重连竞态（已解决）

```
StopAsync                                           重连线程
  _shutdownCts.Cancel()
  PublishDeviceOfflineAsync()                       ConnectAsync(token)
    → success = true                                    → token.IsCancellationRequested → 抛 OCE → catch 吞掉
  _shouldReconnect = false                              → while (_shouldReconnect) → false → 退出循环
  _reconnectLock.WaitAsync                              → _reconnectLock.Release()
    → 拿锁 → DisconnectAsync → done
```

## Key Technical Decisions

| 决策 | 选择 | 理由 | 拒绝方案 |
|------|------|------|---------|
| 单一 topic 三来源 | `events/will` 承载 online/offline/will | retained 覆盖机制要求同一 topic | 三个 topic 无法利用 retained 覆盖 |
| 无参方法 | `PublishDeviceOnlineAsync()` 不传参 | 字段值对主控进程完全固定，无定制需求 | 传参增加调用方出错可能 |
| shutdown Cts | `_shutdownCts` 取消重连 ConnectAsync | 阻止重连成功后 online 覆盖 StopAsync 的 offline | 不取消则存在竞态，Broker 上 retained 错误 |
| offline 失败不 Dispose | 跳过 DisconnectAsync/Dispose，OS 关 TCP | 确保 Broker 看到非正常断连 → 触发 Will | Dispose 内部行为不确定（可能发 DISCONNECT 抑制 Will） |
| Will 字段零值 | `ts=0`, `timestamp="0001-01-01T00:00:00Z"` | Will payload 在 CONNECT 时写死，运行时无法更新 | 动态构造会增加复杂度且 broker 不会帮你填正确值 |
| `mqtt_connected` 去 MQTT 广播 | `events/will` retained online 已覆盖 MQTT 订阅方 | 消除冗余，职责分离 | 两条都发导致前端收两份通知 |
| QoS 1 保留 | 三条消息统一 QoS 1 | 重复投递同一 topic retained 覆盖，不影响幂等性 | QoS 0 可能丢 retained 消息 |

## Test Strategy

### 单元测试：MqttEventPublisher

| 测试 | 验证点 |
|------|--------|
| `PublishDeviceOnlineAsync` 发送正确 topic | topic = `daq/{MachineId}/events/will` |
| `PublishDeviceOnlineAsync` 发送正确 payload | `status:"online"`, `eventType:"device_online"`, `source:"device"`, `ts`≈当前时间, `timestamp`≈当前时间 |
| `PublishDeviceOnlineAsync` retain=true, QoS=1 | `MqttApplicationMessage.Retain == true`, `QualityOfServiceLevel == 1` |
| `PublishDeviceOfflineAsync` 发送正确 payload | `status:"offline"`, `eventType:"device_offline"` |
| `MqttClient` 为 null 时不抛异常 | 只记日志，return |

Mock `IMqttClient`，使用 callback 捕获 `PublishAsync` 的参数做断言。参考现有 `Test/` 项目中使用的测试框架和 mock 方式。

### 单元测试：Will payload

| 测试 | 验证点 |
|------|--------|
| payload 是合法 JSON | `JsonSerializer.Deserialize` 成功 |
| 6 字段齐全 | `status`, `ts`, `eventType`, `source`, `message`, `timestamp` 均存在 |
| 字段值正确 | `status="offline"`, `ts=0`, `eventType="process_crashed"`, `source="mqtt_broker"`, `timestamp` 为零值 |

直接读取 `MqttRpcBackgroundService` 构造时生成的 `_willPayloadBytes` 做反序列化断言。

### 集成测试：SimulationRunner 场景

新增测试场景 JSON 文件，S 阶段验证全链路：

1. 启动 mock 模式 WebAPI → 第三方 MQTT 客户端订阅 `does/+/events/will` → 断言收到 retained online
2. 杀 WebAPI 进程 → 第三方客户端等待 keepalive 超时 → 断言收到 Will retained offline（`eventType="process_crashed"`）
3. 重启 WebAPI → 第三方客户端断言收到 retained online（`eventType="device_online"`）

### 不做测试的内容

- `StopAsync` 和 `ConnectAsync` 的完整时序模拟（需要真实 MQTT Broker，放在集成测试）
- `_shutdownCts` 的竞态条件覆盖（依赖 OS 调度，无法在单元测试中可靠复现）

## Vertical Slice Design

### Slice 1: Foundation — Will payload + MqttEventPublisher（依赖：无）

修改 `MqttRpcBackgroundService` 构造函数（Will payload 新格式），新增 `MqttEventPublisher` 两个方法。编写单元测试验证 payload 和行为。

**产出**：`MqttEventPublisher.PublishDeviceOnlineAsync()` / `PublishDeviceOfflineAsync()` 可用，Will payload 格式正确。

### Slice 2: Wiring — ConnectAsync（依赖：Slice 1）

在 `ConnectAsync` 中接入 `PublishDeviceOnlineAsync`。手动验证 + 集成测试。

**产出**：每次 MQTT 连接成功后，`events/will` 上 retained online 被正确发布。

### Slice 3: Shutdown — StopAsync + 兜底（依赖：Slice 1）

改造 `StopAsync`：加入 `PublishDeviceOfflineAsync`、`_shutdownCts`、publish 失败的兜底逻辑。手动验证正常关闭 → 非正常关闭两种路径。

**产出**：正常下线 Wills 被抑制，异常下线 Wills 触发，竞态受保护。

### Slice 4: Cleanup — SystemStateService（依赖：Slice 2）

移除 `mqtt_connected` 的 MQTT 广播。肉眼检查 `state_changed` topic 不再收到 `mqtt_connected` 事件。

**产出**：职责分离，`events/will` 为 MQTT 连接状态唯一通道。

### Slice 5: E2E — SimulationRunner 场景（依赖：Slice 1-4）

编写集成测试场景，验证崩溃→Wills→重启→online 完整链路。走 SimulationRunner S 阶段。

**产出**：自动化回归测试确保 Will + retained 方案端到端正确。
