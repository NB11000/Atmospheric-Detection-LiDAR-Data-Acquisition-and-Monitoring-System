# 设备在线状态监控：Will + Retained 方案

- **Label**: needs-triage
- **Status**: Draft

## Problem Statement

当前通过 `$SYS` 主题监控设备在线状态，存在两个致命缺陷：

1. **时序依赖导致状态丢失**：`$SYS` 消息仅在设备连接/断开的瞬间发布一次，无 retained 标志。前端晚于设备上线时，错过所有已发布的事件，已在线设备永远显示为离线/未知。前端断线重连后同样无法恢复。
2. **Broker 类型绑定**：`$SYS` 主题格式因 broker 而异（EMQX、Mosquitto、HiveMQ 各不同），换 broker 需要改代码。

用户可见表现：添加设备时"自动发现"列表始终为空，侧边栏中已在线设备始终显示为离线/未知状态。

## Solution

将设备在线状态统一收敛到单一 retained topic `daq/{MachineId}/events/will`，利用 MQTT Will Message + 主动 publish retained 消息，实现时序无关的在线状态监控。

- **设备上线**：CONNACK 成功后，主控进程立即 publish retained online 到 topic
- **设备正常下线**：主控进程 DISCONNECT 前 publish retained offline 到同一 topic，覆盖 online
- **设备异常崩溃**：Broker 检测 keepalive 超时后，自动发布 Will retained offline，覆盖之前状态
- **前端恢复**：任何时候通配符订阅 `daq/+/events/will`，Broker 立即推送所有设备的最新 retained 状态

## User Stories

1. As an 仪表盘用户, I want 打开页面后立即看到每条设备的在线/离线状态, so that 我不需要等设备下一次上下线事件才能知道当前状态
2. As an 仪表盘用户, I want 设备崩溃时能立即感知并看到离线提示, so that 运维可以及时响应
3. As an 运维人员, I want 切换到不同品牌的 MQTT Broker 时不需要改代码, so that 部署灵活性不受限
4. As an 运维人员, I want 仪表盘断线重连后自动恢复设备状态显示, so that 不会因为临时网络问题遗留错误状态
5. As a 开发人员, I want 设备在线状态的发布逻辑与业务状态变更（采集卡/激光器）走不同通道, so that 职责清晰互不干扰
6. As a 开发人员, I want 正常关闭服务时前端能准确看到设备下线, so that 不会误以为设备崩溃
7. As a 部署人员, I want `MachineId` 标识从 topic 路径就能提取, so that 仪表盘不需要依赖 payload 里的额外字段做设备映射

## Implementation Decisions

### 模块划分

| 模块 | 改动类型 | 职责 |
|------|---------|------|
| MqttEventPublisher | 新增方法 | `PublishDeviceOnlineAsync()` / `PublishDeviceOfflineAsync()`，内部固定填充 6 字段 payload，retain=true，发往 `events/will` |
| MqttRpcBackgroundService | 修改 | 构造 Will payload 为新格式；`ConnectAsync` 在 Subscribe 后调 `PublishDeviceOnlineAsync`；`StopAsync` 在 DisconnectAsync 前调 `PublishDeviceOfflineAsync`，失败则跳过 DisconnectAsync 由 Will 兜底；新增 `_shutdownCts` 取消重连中的 ConnectAsync |
| SystemStateService | 修改 | `mqtt_connected` 去掉 MQTT `BroadcastAsync`，仅保留 SignalR 推送 + 内部事件 `MqttConnectionStateChanged` |

### Payload 契约

统一 6 字段 JSON，三种场景共用同一结构：

| 字段 | 类型 | 说明 |
|------|------|------|
| `status` | string | `"online"` / `"offline"` |
| `ts` | long | Unix 毫秒时间戳（Will 为 `0`） |
| `eventType` | string | `"device_online"` / `"device_offline"` / `"process_crashed"` |
| `source` | string | `"device"` / `"mqtt_broker"` |
| `message` | string | 可读描述 |
| `timestamp` | DateTime | UTC 时间（Will 为零值） |

`deviceId` 不从 payload 携带，由订阅方从 topic 路径 `daq/{MachineId}/events/will` 第二段提取。

### 架构决策

- **单 topic 三来源**：同一 topic 承载主动 online、主动 offline、Will offline 三种消息，retained 覆盖保证任何时刻订阅方拿到的是最新状态
- **offline publish 失败兜底**：正常下线时如果 publish 失败（MQTT 通道已断），跳过 DisconnectAsync，进程退出时 OS 关闭 TCP → Broker 触发 Will → retained offline 最终正确
- **重连竞态保护**：`StopAsync` 通过 `_shutdownCts.Cancel()` 终止正在进行的重连 `ConnectAsync`，防止重连成功后 publish online 覆盖刚发出的 offline
- **QoS 统一 1**：三条消息均为 QoS 1，重复投递不影响幂等性（同一 topic retained 覆盖）
- **`state_changed` 职责收缩**：`mqtt_connected`/`mqtt_disconnected` 的 MQTT 广播停发——MQTT 通道断了发不出去，恢复了由 `events/will` retained 覆盖。`state_changed` 仍通过 SignalR 推送并触发内部事件
- **Will payload 构造函数硬编码**：Will 字段值（`status="offline"`, `ts=0`, `eventType="process_crashed"`）永久不变，不在运行时动态构造

### 与现有 system 的交互

- `MqttConnectionStateChanged` 内部事件不受影响，采集生命周期协调器继续正常工作
- `SystemStateDto.Server.IsMqttConnected` 字段继续由 `SystemStateService` 维护
- `MqttEventPublisher.MqttClient` 属性继续由 `MqttRpcBackgroundService` 注入
- `OnDisconnectedAsync` 逻辑不变（被动断连走原重连路径，`ConnectAsync` 里会发布 online）

## Testing Decisions

- **MqttEventPublisher 新增方法**：单元测试，mock `IMqttClient`，验证 payload 字段值、topic 组装、retain=true、QoS=1
- **Will payload 格式**：单元测试，验证构造函数生成的 JSON 字符串格式正确、6 字段齐全
- **完整 Will + retained 生命周期**：集成测试走 SimulationRunner + mock 模式 + 真实 MQTT Broker，验证：进程崩溃后 Will 触发 → 第三方客户端收到 retained offline → 重启后收到 retained online
- **测试原则**：只测试外部可见行为（topic + payload），不耦合内部调用链细节。参考现有 CoreDataBus 测试的 Arrange-Act-Assert 模式

## Out of Scope

- 前端 `data-acquisition-web` 的 MQTT 客户端接入（对接方不在当前工作区）
- `events/state_changed` topic 的完整重构（预留后续优化）
- 子进程（ConsoleApp1）的独立 MQTT 连接和在线状态上报
- 多设备部署场景下的自动发现和配置下发

## Further Notes

- `events/will` 主题名称保留不变，但语义从纯"遗嘱"扩展为"设备在线状态"——历史订阅方需同步更新 payload 解析逻辑
- keepalive 窗口最多 ~45 秒（30s × 1.5），此期间若 publish offline 失败且进程退出，仪表盘短暂以为是 online 状态
- 已有 ADR `0004-device-online-status-via-will-retained.md` 记录本次架构决策
- `MQTT主题文档.md` 和 `通用设备在线监控方案.md` 已同步更新为 6 字段新格式
