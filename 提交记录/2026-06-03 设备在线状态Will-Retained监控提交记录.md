# 提交记录

> 生成时间：2026-06-03
> 仓库：数据采集与检测系统 V2.0
> 分支：`main`

---

## 一、背景（Background）

当前设备在线状态依赖 MQTT broker 内部的 `$SYS` 主题，存在两个致命缺陷：`$SYS` 消息仅在设备连接/断开瞬间发布一次，无 retained 标志，前端晚连接则永远错过历史状态；`$SYS` 格式因 broker 而异，换 broker 必改代码。

决定采用 ADR-0004 方案：将设备在线状态统一收敛到 `daq/{MachineId}/events/will` 单一 retained topic，利用 MQTT Will Message + 主动 publish retained 消息实现时序无关的在线状态监控。

### 问题（Problem）

#### 1. 时序依赖导致状态丢失

`$SYS` 消息非 retained，前端晚于设备上线时错过所有已发布事件，已在线设备永远显示为离线/未知。前端断线重连后同样无法恢复。

#### 2. Broker 类型绑定

`$SYS` 主题格式因 broker 而异，换 broker（EMQX/Mosquitto/HiveMQ）需改代码。

#### 3. publish 失败的离线状态丢失风险

正常关闭时 publish offline 消息若因网络已断开而失败，正常 DISCONNECT 会抑制 Will 触发，导致 Broker 上 retained 仍为 online。

#### 4. 重连与停服竞态

`StopAsync` 期间如果重连线程刚好完成 `ConnectAsync`，其内部 publish online 会覆盖 `StopAsync` 刚发布的 offline。

---

## 二、解决方案（Solution）

### 整体思路

三种来源（主动 online / 主动 offline / Will 崩溃）打在同一个 retained topic 上，利用 retained 覆盖机制保证任何时候订阅方拿到的都是最新状态。不再依赖 broker 内部 `$SYS`。

### 具体实施

#### 1. 统一 Payload 格式（6 字段）

`status` + `ts` + `eventType` + `source` + `message` + `timestamp`。`deviceId` 不冗余携带——从 topic 路径 `daq/{MachineId}/events/will` 第二段提取。`reason` 字段废弃。

三种场景：

| 场景 | status | eventType | source | ts |
|------|--------|-----------|--------|----|
| 主动上线 | online | device_online | device | 当前 ms |
| 主动下线 | offline | device_offline | device | 当前 ms |
| Will 崩溃 | offline | process_crashed | mqtt_broker | 0 |

#### 2. MqttEventPublisher 新增两个 retained 发布方法

`PublishDeviceOnlineAsync()` / `PublishDeviceOfflineAsync()` — 无参，内部固定填充 payload，retain=true，QoS=1。内部共用一个 `PublishWillPayloadAsync` 私有方法。

#### 3. ConnectAsync 接入 online publish

`SubscribeAsync` 完成后、`UpdateMqttConnectionState` 之前调用 `PublishDeviceOnlineAsync`。每次 CONNACK（含重连）都发布，覆盖 Broker 上可能残留的 Will offline。

#### 4. StopAsync 接入 offline publish + 兜底

publish offline 成功 → 正常 DisconnectAsync（Will 不触发）。publish 失败 → 跳过 DisconnectAsync/Dispose，进程退出时 OS 关闭 TCP → Broker 触发 Will → retained offline。

#### 5. `_shutdownCts` 竞态保护

`StopAsync` 开头 Cancel `_shutdownCts`。`ConnectAsync` 使用 linked token——确保 StopAsync 期间重连中的 ConnectAsync 被取消，不会 publish online 覆盖 offline。

#### 6. SystemStateService 去冗余 MQTT 广播

`mqtt_connected` 不再通过 BroadcastAsync 推送 MQTT `state_changed`。MQTT 连接状态由 `events/will` retained 消息担任唯一推送通道。SignalR 推送和内部事件保留。

---

## 三、Git 提交消息

```
feat(MQTT): 通过 Will + Retained 机制实现设备在线状态监控，取代 $SYS 主题
```

**正文：**

1. MqttEventPublisher 新增 PublishDeviceOnlineAsync/PublishDeviceOfflineAsync retained 发布方法
2. MqttRpcBackgroundService ConnectAsync 接入 online publish，StopAsync 接入 offline publish + 失败兜底
3. StopAsync 新增 _shutdownCts 取消重连中的 ConnectAsync，防止竞态覆盖 offline
4. SystemStateService 去掉 mqtt_connected 的 MQTT state_changed 广播，职责移交 events/will
5. Will payload 更新为统一 6 字段格式，废弃 reason 字段
6. 新增 4 个测试类：MqttEventPublisher 单元测试、ConnectAsync/StopAsync 行为测试、E2E 集成测试
7. 新增 ADR-0004 文档、前端对接文档，更新 MQTT 主题文档与设备在线监控方案文档

---

## 四、本次提交详情

### 基本信息

| 字段 | 内容 |
|------|------|
| **提交时间** | 2026-06-03 |
| **作者** | NB11000 |
| **基于提交** | `5d6de5b` — test(Persistence): 新增 PersistenceService 集成测试，CoreDataBus 添加 Write 支持 (2026-06-01 22:32) |
| **变更统计（核心 20 文件）** | 20 files changed, +2099 insertions, -27 deletions |

### 核心变更文件清单

| 状态 | 文件路径 | 变更说明 |
|------|----------|----------|
| 修改 | `WebAPI/Service/MqttEventPublisher.cs` | 新增 PublishDeviceOnlineAsync/PublishDeviceOfflineAsync/PublishWillPayloadAsync（+63 行） |
| 修改 | `WebAPI/Service/MqttRpcBackgroundService.cs` | Will payload 6 字段 + ConnectAsync online publish + StopAsync offline 兜底 + _shutdownCts（+61/-27 行） |
| 修改 | `WebAPI/Service/SystemStateService.cs` | 移除 mqtt_connected MQTT 广播 + ServerStateDto 新增 IsMqttConnected（+17 行） |
| 修改 | `MQTT主题文档.md` | events/will 章节重写为三来源统一格式（+45 行） |
| 修改 | `通用设备在线监控方案.md` | payload 格式更新为实际采用的 6 字段规范 |
| 新建 | `前端对接-设备在线状态监控.md` | 前端对接完整手册（+229 行） |
| 新建 | `docs/adr/0004-device-online-status-via-will-retained.md` | ADR 架构决策记录（+18 行） |
| 新建 | `Test/WebAPI.Tests/MqttEventPublisherOnlineStatusTests.cs` | 258 行单元测试 |
| 新建 | `Test/WebAPI.Tests/MqttRpcBackgroundServiceConnectTests.cs` | 165 行行为测试 |
| 新建 | `Test/WebAPI.Tests/MqttRpcBackgroundServiceStopAsyncTests.cs` | 153 行行为测试 |
| 新建 | `Test/WebAPI.Tests/WillRetainedE2EIntegrationTests.cs` | 362 行端到端集成测试 |
| 修改 | `Test/WebAPI.Tests/SystemStateServiceMqttConnectionTests.cs` | 去 MQTT 广播验证（+61 行） |
| 新建 | `.scratch/device-online-status/PRD.md` | 产品需求文档（+94 行） |
| 新建 | `.scratch/device-online-status/IMPLEMENTATION_PLAN.md` | 实施计划（+209 行） |
| 新建 | `.scratch/device-online-status/issues/01-05` | 5 个 TDD 垂直切片 Issue |
| 新建 | `.scratch/device-online-status/scenarios/05-will-retained-e2e.json` | E2E 测试场景 JSON（+86 行） |

---

## 五、架构影响

| 维度 | 变更前 | 变更后 |
|------|--------|--------|
| 设备在线状态通道 | `$SYS` 主题（broker 绑定，无 retained） | `events/will` topic（Will + retained，协议级通用） |
| 在线消息发布者 | Broker 内部自动 | 主控进程主动 publish + Broker Will 兜底 |
| 上线事件路径 | `state_changed` topic（非 retained） | `events/will` topic（retained） + state_changed 仅 SignalR |
| 离线事件路径 | Will → `events/will`（retained true） | 主动 publish retained → 正常断；publish 失败 → Will 兜底 |
| Payload 格式 | 3 字段（eventType/source/message）+ reason | 6 字段（status/ts/eventType/source/message/timestamp） |

```
CONNACK 成功
  ├── PublishDeviceOnlineAsync() → events/will, retained online     ← 新增
  └── UpdateMqttConnectionState(true)
       ├── SignalR 推送                                              ← 保留
       ├── MqttConnectionStateChanged 事件                           ← 保留
       └── state_changed MQTT 广播                                   ← 移除

StopAsync
  ├── _shutdownCts.Cancel()                                         ← 新增
  ├── PublishDeviceOfflineAsync() → events/will, retained offline   ← 新增
  │    ├── 成功 → DisconnectAsync                                   ← 正常
  │    └── 失败 → 跳过 DisconnectAsync → OS 关 TCP → Will 触发     ← 新增兜底
  └── UpdateMqttConnectionState(false)                               ← 保留
```

---

## 六、审核报告

> 审查范围：`MqttEventPublisher.cs`、`MqttRpcBackgroundService.cs`、`SystemStateService.cs`、全部测试文件

### 通过项

| # | 检查点 | 详情 |
|---|--------|------|
| 1 | retained 覆盖正确性 | 三条消息同 topic，QoS 1 重复投递幂等 |
| 2 | offline 兜底 | publish 失败 → 跳过 DisconnectAsync → Will 触发，无窗口期外数据丢失 |
| 3 | 竞态保护 | _shutdownCts 取消重连 ConnectAsync → linked token 传播取消 |
| 4 | 向后兼容 | events/will topic 名不变，payload 扩展为 6 字段，订阅方需同步更新解析逻辑 |
| 5 | 单元测试覆盖 | MqttEventPublisher 方法 topic/payload/retain/QoS + null/未连接边界 + SystemStateService 去广播验证 |
| 6 | 集成测试覆盖 | E2E 场景：启动→online→杀进程→Will offline→重启→online |

### 遗留建议（非阻塞）

| # | 严重度 | 位置 | 建议 |
|---|--------|------|------|
| 1 | 低 | `MqttRpcBackgroundService.BuildWillPayloadBytes` | `ts:0` 和零值 `timestamp` 为 Will 固定值，未来如需 Broker 端时间戳可考虑服务端自行记录 |
| D36 | 低 | `StopAsync` | publish offline 失败跳过 Dispose——MqttClient 不 Dispose 造成的内存泄漏可忽略（进程退出时 OS 回收） |

---

## 七、后续步骤预览（不在本次范围）

- 前端 `data-acquisition-web` 接入 MQTT 客户端，订阅 `daq/+/events/will` 实现设备在线面板
- `events/state_changed` 主题完整重构（采集卡/激光器状态变更的发布职责重新划分）
- 多机部署场景的自动发现与配置下发
