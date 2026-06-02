# 数据采集与检测系统 V2.0 — MQTT 主题文档

> Brocker: `z0d131fe.ala.cn-hangzhou.emqxsl.cn:8883`（EMQX Serverless，MQTTS/TLS 1.2+）  
> 认证: 用户名/密码 (`001`/`001`) + CA 证书 `emqxsl-ca.crt`  
> 协议: MQTTnet 5.1.0 | 文档版本: 2026-05-08

---

## 1 主题命名规范

```
{功能域}/{机器标识}/{模块}/{资源}[/{子资源}...]
```

| 层级 | 说明 | 示例值 |
|------|------|--------|
| 功能域 | `$rpc` = 请求-响应调用，`daq` = 数据/事件推送 | `$rpc`, `daq` |
| 机器标识 | 设备唯一 ID，取自 `appsettings.json → Mqtt.MachineId` | `daq-srv-01`，多机部署时按设备分配 |
| 模块 | 功能归类（`waveform`、`events` 等） | `waveform`, `events` |
| 资源 | 通道号 / 方法名 / 事件类型 | `ch1`, `state_changed` |

**RPC 特化模板**

```
$rpc/{MachineId}/{方法名}/{关联ID}
$rpc/{MachineId}/{方法名}/{关联ID}/response     ← 响应主题（MQTT 5.0 ResponseTopic 属性优先）
```

**变量说明**：`{关联ID}` 由调用方生成（如 GUID），用于匹配请求与响应。

---

## 2 功能主题清单

### 2.1 设备数据上报

#### 2.1.1 波形数据（高频流式）

| 项目 | 通道 1 | 通道 2 |
|------|--------|--------|
| **主题** | `daq/{MachineId}/waveform/ch1` | `daq/{MachineId}/waveform/ch2` |
| **发布者** | WebAPI 主控进程 | 同左 |
| **订阅者** | 远程监控前端 / 仪表盘 | 同左 |
| **QoS** | 0（至多一次） | 0 |
| **Retain** | 否 | 否 |
| **发布间隔** | 100 ms（可配置 `WaveformPublishIntervalMs`） | 同左 |
| **负载格式** | **二进制** `double[1000]` 小端序，8000 字节 | 同左 |

> **解析示例（C#）**: `Buffer.BlockCopy(payload, 0, buffer, 0, 8000);` — 所得为 1000 点 `double[]`。  
> **禁止用 JSON 解析**此主题负载。

#### 2.1.2 数据更新事件

| 项目 | 说明 |
|------|------|
| **主题** | `daq/{MachineId}/events/data_updated` |
| **发布者** | `MqttEventPublisher`（WebAPI 主控进程） |
| **订阅者** | 远程监控前端 / 数据分析服务 |
| **QoS** | 0（至多一次） |
| **Retain** | 否 |

**负载 JSON**

```json
{
  "dataType": "waveform_frame",
  "data": { /* 任意数据对象，取决于 dataType */ },
  "timestamp": "2026-04-28T15:00:00"
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `dataType` | `string` | 数据类型标识，如 `waveform_frame` |
| `data` | `object` | 携带的数据体 |
| `timestamp` | `DateTime` | 事件时间戳 |

> **注意**：该主题对应的方法 `PublishDataUpdatedAsync` 当前无调用方，处于待启用状态。

---

#### 2.1.3 低频采样数据

| 项目 | 说明 |
|------|------|
| **主题** | `daq/{MachineId}/lowfreq` |
| **发布者** | `LowFrequencyPublisher`（WebAPI 主控进程，采集绑定） |
| **订阅者** | 远程监控前端 / 数据分析平台 |
| **QoS** | 1（至少一次） |
| **Retain** | 否 |
| **发布间隔** | 7 s（固定） |

**负载 JSON**

```json
{
  "timestamp": 123456,
  "utc": "2026-04-28T15:00:00.0000000Z",
  "ch1": 1.234,
  "ch2": -0.567,
  "vis": 12.345,
  "cn2": 8.90e-13,
  "temp": 22.5,
  "humi": 60.2,
  "press": 1013.25,
  "windSpd": 2.5,
  "rain": 0.0,
  "windDir": 180.0
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `timestamp` | `long` | 采样点序号（session-local 递增） |
| `utc` | `string` | UTC 绝对时间（ISO 8601 格式） |
| `ch1` | `double` | 通道 1 电压值 |
| `ch2` | `double` | 通道 2 电压值 |
| `vis` | `double` | 能见度（km），整帧共享 |
| `cn2` | `double` | 折射率结构常数（m⁻²/³） |
| `temp` | `double` | 温度（℃） |
| `humi` | `double` | 相对湿度（%） |
| `press` | `double` | 大气压力（hPa） |
| `windSpd` | `double` | 风速（m/s） |
| `rain` | `double` | 雨量（mm） |
| `windDir` | `double` | 风向（°） |

> 本质为周期性快照抽样（7s 取一次最新值），非全量归档。12 字段对应 `StructuredSample` 完整结构。

---

### 2.2 命令下发

统一使用 RPC 模式：调用方发布请求 → WebAPI 处理后发布响应。

#### 2.2.1 RPC 请求主题

| 项目 | 说明 |
|------|------|
| **主题** | `$rpc/{MachineId}/{方法名}/{关联ID}` |
| **发布者** | 远程 UI / 控制台 / 三方平台 |
| **订阅者** | WebAPI 主控进程（订阅 `$rpc/{MachineId}/#`） |
| **QoS** | 1（至少一次） |
| **Retain** | 否 |

#### 2.2.2 RPC 响应主题

| 项目 | 说明 |
|------|------|
| **主题** | `$rpc/{MachineId}/{方法名}/{关联ID}/response` |
| **发布者** | WebAPI 主控进程 |
| **订阅者** | 原 RPC 调用方 |
| **QoS** | 1（至少一次） |
| **Retain** | 否 |

#### 2.2.3 可用方法一览

**采集卡控制**

| 方法名 | 请求负载 | 响应负载类型 | 对应功能 |
|--------|----------|-------------|----------|
| `collector-status` | 空 | JSON 对象 | 查询子进程连接状态 |
| `collector-command-send` | 纯文本指令，如 `"OPEN_DEVICE"` | gRPC 响应 | 同步发送指令 |
| `collector-command-send-async` | 纯文本指令，如 `"START_AD"` | `{"accepted":true}` | 异步发送指令 |
| `collector-open-device` | 空 | `CommandResult` | 打开采集卡 |
| `collector-open-device-again` | 空 | `CommandResult` | 重新打开采集卡 |
| `collector-close-device` | 空 | `CommandResult` | 关闭采集卡 |
| `collector-start-ad` | 空 | `CommandResult` | 开始采集 |
| `collector-stop-ad` | 空 | `CommandResult` | 停止采集 |
| `collector-ping` | 空 | gRPC 响应 | 心跳检测 |
| `collector-exit` | 空 | gRPC 响应 | 优雅退出子进程 |

**激光器控制**

| 方法名 | 请求负载 | 响应负载类型 |
|--------|----------|-------------|
| `laser-connect` | 空 | `CommandResult` |
| `laser-disconnect` | 空 | `CommandResult` |
| `laser-on` | 空 | `CommandResult` |
| `laser-off` | 空 | `CommandResult` |
| `laser-status` | 空 | JSON 对象（见 2.2.5） |

#### 2.2.4 CommandResult（统一响应）

```json
{
  "success": true,
  "code": "COLLECTOR_OPENED",
  "message": "设备打开成功",
  "state": { /* SystemStateDto，为 null 时不序列化 */ },
  "timestamp": "2026-04-28T15:00:00"
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `success` | `bool` | 是否成功 |
| `code` | `string` | 结果码（`COLLECTOR_OPENED`、`AD_STARTED`、`LASER_CONNECTED` 等） |
| `message` | `string` | 用户可读消息 |
| `state` | `SystemStateDto?` | 操作后系统状态快照（null 时不序列化） |
| `timestamp` | `DateTime` | 响应时间戳 |

**RPC 错误响应**

```json
{"success":false,"code":"UNKNOWN_METHOD","message":"未知的 RPC 方法: xxx"}
{"success":false,"code":"HANDLER_EXCEPTION","message":"服务端处理异常: xxx"}
```

#### 2.2.5 laser-status / collector-status 专属响应

**laser-status**

```json
{"connected":true,"emissionOn":false,"portName":"COM3","timestamp":"2026-04-28T15:00:00"}
```

**collector-status**

```json
{"clientId":"数据采集子进程","connected":true,"timestamp":"2026-04-28T15:00:00"}
```

#### 2.2.6 SystemStateDto（命令响应中 state 字段的完整结构）

```json
{
  "server": {"isApiAlive": true, "timestamp": "..."},
  "collector": {
    "processConnected": true, "deviceOpened": false,
    "acquiring": false, "handle": 0,
    "lastMessage": "", "timestamp": "..."
  },
  "laser": {
    "serialConnected": false, "emissionOn": false,
    "portName": "", "lastMessage": "", "timestamp": "..."
  },
  "uiHints": {
    "canOpenCollector": true, "canCloseCollector": false,
    "canStartAcquisition": false, "canStopAcquisition": false,
    "canConnectLaser": true, "canDisconnectLaser": false,
    "canTurnLaserOn": false, "canTurnLaserOff": false
  },
  "timestamp": "2026-04-28T15:00:00"
}
```

---

### 2.3 状态心跳

#### 2.3.1 状态变更推送（核心心跳通道）

| 项目 | 说明 |
|------|------|
| **主题** | `daq/{MachineId}/events/state_changed` |
| **发布者** | `MqttEventPublisher`（WebAPI 主控进程） |
| **订阅者** | 所有监控方 |
| **QoS** | 1 |
| **Retain** | 否 |

**负载 JSON**

```json
{
  "eventType": "collector_connected",
  "source": "collector",
  "reason": "数据采集子进程连接已建立",
  "message": "数据采集子进程连接已建立",
  "state": { /* SystemStateDto（为 null 时不序列化） */ },
  "timestamp": "2026-04-28T15:00:00"
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `eventType` | `string` | 16 种事件类型，定义于 `WebAPI/Models/StateChangeEvents.cs`：`collector_connected`、`collector_disconnected`、`device_opened`、`device_closed`、`acquisition_started`、`acquisition_stopped`、`device_disconnected`、`acquisition_failed`、`device_open_failed`、`laser_connected`、`laser_disconnected`、`laser_on`、`laser_off`、`error`、`mqtt_connected`、`mqtt_disconnected`。遗嘱消息独立使用 `process_crashed`（由 Broker 发布，不在此常量类中） |
| `source` | `string` | `collector`（采集卡事件）、`laser`（激光器事件）、`system`（MQTT 连接/断开等系统级事件）、`mqtt_broker`（遗嘱消息） |
| `reason` | `string` | 变更原因 |
| `message` | `string` | 可读描述 |
| `state` | `SystemStateDto?` | 当前系统状态快照 |
| `timestamp` | `DateTime` | 事件时间戳 |

#### 2.3.1a 设备在线状态 — `daq/{MachineId}/events/will`

设备在线状态统一收敛到单一 retained topic，利用 Will + 主动 publish 覆盖实现时序无关的在线监控。同一条 topic 承载三种来源：

| 场景 | 发布者 | 触发时机 |
|------|--------|---------|
| 设备上线 | WebAPI 主控进程 | CONNACK 成功后立即 publish |
| 设备正常下线 | WebAPI 主控进程 | 正常 DISCONNECT 前 publish |
| 设备异常崩溃 | EMQX Broker（遗嘱消息） | keepalive 超时后自动发布 |

| 项目 | 说明 |
|------|------|
| **主题** | `daq/{MachineId}/events/will` |
| **QoS** | 1（至少一次） |
| **Retain** | **true**（新订阅者立即获取最新状态，后续实时增量更新） |

**负载 JSON（6 字段统一格式）**

```json
{
  "status": "online",
  "ts": 1717400000000,
  "eventType": "device_online",
  "source": "device",
  "message": "设备已上线",
  "timestamp": "2026-06-03T10:30:00.0000000Z"
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `status` | `string` | `"online"` / `"offline"` |
| `ts` | `long` | Unix 毫秒时间戳（Will 消息固定为 `0`） |
| `eventType` | `string` | `"device_online"` / `"device_offline"` / `"process_crashed"` |
| `source` | `string` | `"device"`（主动） / `"mqtt_broker"`（遗嘱） |
| `message` | `string` | 可读描述 |
| `timestamp` | `DateTime` | UTC 时间（Will 消息为零值 `0001-01-01T00:00:00Z`） |

**三种场景对比**

| 场景 | status | eventType | source | ts | message | timestamp |
|------|--------|-----------|--------|----|---------|-----------|
| 主动上线 | `"online"` | `"device_online"` | `"device"` | 当前 ms | 设备已上线 | 当前 UTC |
| 主动下线 | `"offline"` | `"device_offline"` | `"device"` | 当前 ms | 设备正常下线 | 当前 UTC |
| 遗嘱崩溃 | `"offline"` | `"process_crashed"` | `"mqtt_broker"` | `0` | 设备已离线 | 零值 |

> **注意**：`state_changed` topic 中 `mqtt_connected` / `mqtt_disconnected` 的 MQTT 广播已停发——MQTT 连接状态变更通过本 topic 的 retained 消息覆盖实现，前端通过通配符 `daq/+/events/will` 订阅所有设备的在线状态。`MachineId` 从 topic 路径第二段提取，payload 不冗余携带。

#### 2.3.2 主动查询心跳

调用 `system-state` RPC 方法获取即时快照：

- **请求主题**: `$rpc/{MachineId}/system-state/{关联ID}`（无负载）
- **响应主题**: `$rpc/{MachineId}/system-state/{关联ID}/response`
- **响应负载**: `SystemStateDto`（见 §2.2.6）

---

### 2.4 告警通知

#### 2.4.1 设备报警

| 项目 | 说明 |
|------|------|
| **主题** | `daq/{MachineId}/events/device_alarm` |
| **发布者** | `MqttEventPublisher`（WebAPI 主控进程） |
| **订阅者** | 告警管理平台 / 运维监控端 |
| **QoS** | 1（至少一次） |
| **Retain** | **true**（新订阅者能获取当前最新报警状态） |

**负载 JSON**

```json
{
  "alarmType": "device_error",
  "device": "collector",
  "message": "采集卡数据读取失败",
  "severity": 3,
  "timestamp": "2026-04-28T15:00:00"
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `alarmType` | `string` | 报警类型（`device_error` 等） |
| `device` | `string` | 设备名（`collector`、`laser` 等） |
| `message` | `string` | 报警详情 |
| `severity` | `int` | 严重程度 1–5，5 最高 |
| `timestamp` | `DateTime` | 报警时间戳 |

---

#### 2.4.2 检测告警

| 项目 | 说明 |
|------|------|
| **主题** | `daq/{MachineId}/detection/alerts` |
| **发布者** | `DetectionPublisherService`（WebAPI 主控进程，采集绑定） |
| **订阅者** | 告警管理平台 / 运维监控端 |
| **QoS** | 1（至少一次） |
| **Retain** | 否 |
| **触发方式** | 检测线程通过 gRPC 双向流上报 → `GrpcServiceImpl` 路由 → `DetectionPublisherService.OnAlertReceived()` → 异步发布 |

**负载 JSON**

```json
{
  "alarmType": "obstruction",
  "severity": "High",
  "timestamp": 1234567890,
  "ch1": 0.002,
  "ch2": 0.003
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `alarmType` | `string` | 告警类型（`obstruction` = 信号遮挡，`condition` = 工况异常，`jump` = 跳变） |
| `severity` | `string` | 严重程度（`Low` / `Medium` / `High` / `Critical`） |
| `timestamp` | `long` | 告警触发时的采样点序号 |
| `ch1` | `double` | 触发时通道 1 电压值 |
| `ch2` | `double` | 触发时通道 2 电压值 |

> 与 2.4.1 设备报警的区别：设备报警关注采集卡/激光器等硬件异常，检测告警关注数据层面（遮挡、工况、跳变）。

---

### 2.5 系统配置更新

配置读写通过 RPC 命令下发实现（复用 §2.2 命令下发框架）。

| 方法名 | 请求负载 | 响应负载类型 | 功能 |
|--------|----------|-------------|------|
| `collector-config-read` | 空 | `CaptureCardConfig` | 读取采集卡配置 |
| `collector-config-update` | `CaptureCardConfig`（JSON） | `CaptureCardConfig` | 更新采集卡配置 |
| `collector-config-default` | 空 | `CaptureCardConfig` | 获取默认采集卡配置 |
| `laser-config-read` | 空 | `RadarConfig` | 读取激光雷达配置 |
| `laser-config-update` | `RadarConfig`（JSON） | `RadarConfig` | 更新激光雷达配置 |
| `lidar-config-read` | 空 | `LidarAlgorithmConfig` | 读取激光雷达算法配置 |
| `lidar-config-update` | `LidarAlgorithmConfig`（JSON） | `LidarAlgorithmConfig` | 更新激光雷达算法配置 |
| `persistence-config-read` | 空 | `PersistenceSettings` | 读取数据持久化配置 |
| `persistence-config-update` | `PersistenceSettings`（JSON） | `PersistenceSettings` | 更新数据持久化配置 |

#### CaptureCardConfig

```json
{
  "deviceId": 0,
  "syncChannelIndex": 2,
  "sampleRate": 1000,
  "clockSourceIndex": 0,
  "halfFullThreshold": 5,
  "triggerSourceIndex": 1,
  "rangeIndex": 0
}
```

| 字段 | 类型 | 说明 | 取值范围 |
|------|------|------|----------|
| `deviceId` | `int` | 设备编号 | 0 |
| `syncChannelIndex` | `int` | 同步通道 | 0=通道1, 1=通道2, 2=双通道 |
| `sampleRate` | `decimal` | 采样频率 (kHz) | 如 1000 |
| `clockSourceIndex` | `int` | 时钟源 | 0=内时钟, 1=外时钟 |
| `halfFullThreshold` | `int` | 半满阈值编号 | 0=2M ~ 7=16K |
| `triggerSourceIndex` | `int` | 触发源 | 0=外触发, 1=软触发 |
| `rangeIndex` | `int` | 量程 | 0=±5V, 1=±10V |

#### RadarConfig

```json
{
  "laserPower": 100,
  "laserModulationFrequency": 1000,
  "serialPort": "COM3",
  "baudRate": 9600
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `laserPower` | `int` | 激光功率 0–100 |
| `laserModulationFrequency` | `int` | 调制频率 |
| `serialPort` | `string` | 串口号 |
| `baudRate` | `int` | 波特率 |

#### LidarAlgorithmConfig

```json
{
  "gainEqualizationCoefficient": 1.0,
  "kConstant": 4.48,
  "receiverApertureD_m": 0.2,
  "pathLengthL_m": 1000.0,
  "cn2WindowFrames": 100,
  "fernaldBoundaryDistance_m": 3000.0,
  "laserWavelength_nm": 532.0,
  "angstromExponent": 1.3,
  "darkCurrentSampleCount": 0,
  "sampleRateHz": 20000000.0,
  "blindZoneDistance_m": 30.0
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `gainEqualizationCoefficient` | `double` | 增益均衡系数 |
| `kConstant` | `double` | K 常数 |
| `receiverApertureD_m` | `double` | 接收孔径直径 (m) |
| `pathLengthL_m` | `double` | 传输路径长度 (m) |
| `cn2WindowFrames` | `int` | Cn² 计算窗口帧数 |
| `fernaldBoundaryDistance_m` | `double` | Fernald 边界距离 (m) |
| `laserWavelength_nm` | `double` | 激光波长 (nm) |
| `angstromExponent` | `double` | Ångström 指数 |
| `darkCurrentSampleCount` | `int` | 暗电流采样点数 |
| `sampleRateHz` | `double` | 采样率 (Hz) |
| `blindZoneDistance_m` | `double` | 盲区距离 (m) |

#### PersistenceSettings

```json
{
  "dataDirectory": "data"
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `dataDirectory` | `string` | 数据持久化目录路径 |

---

## 3 QoS 与 Retain 速查表

| 主题 | QoS | Retain | 策略理由 |
|------|-----|--------|----------|
| RPC 请求 `$rpc/+/#` | 1 | 否 | 命令不可丢失 |
| RPC 响应 | 1 | 否 | 响应必须送达调用方 |
| `waveform/ch1`, `ch2` | 0 | 否 | 高频流（100ms/帧），丢帧优于积压 |
| `lowfreq` | 1 | 否 | 低频快照不可丢失 |
| `state_changed` | 1 | 否 | 状态变更不可丢，但不需回放 |
| `events/will`（设备状态） | 1 | **是** | Retained 覆盖实现时序无关监控，三种来源统一 topic |
| `device_alarm` | 1 | **是** | 报警必须送达，覆盖旧值 |
| `detection/alerts` | 1 | 否 | 检测告警不可丢失 |
| `data_updated` | 0 | 否 | 低延迟优先，允许丢帧（当前无调用方） |

---

## 4 通配符与安全

### 4.1 通配符规则

| 通配符 | 含义 | 支持订阅 |
|--------|------|----------|
| `+` | 匹配**单级**路径 | `$rpc/+/waveform/#` |
| `#` | 匹配**所有后续**层级（必须放在末尾） | `daq/+/events/#` |

**推荐订阅模式**：

```
$rpc/{MachineId}/#              ← 服务端订阅本机所有 RPC 请求
daq/{MachineId}/waveform/#      ← 订阅本机波形
daq/{MachineId}/events/#        ← 订阅本机事件（含 will / state_changed / device_alarm / data_updated）
daq/{MachineId}/lowfreq         ← 订阅本机低频采样数据
daq/{MachineId}/detection/#     ← 订阅本机检测告警
daq/+/events/state_changed      ← 跨设备统一监控
```

### 4.2 安全建议

1. **EMQX 侧配置 ACL 规则**，限制每个客户端仅能订阅/发布其授权主题范围。
   - `{用户名}` 与 `{MachineId}` 建议一一绑定，避免 A 设备客户端订阅 B 设备的 RPC 命令。
2. **波形主题不下放给非必要客户端**，`daq/+/waveform/#` 带宽密集（16 KB/100ms），仅授权给监控终端。
3. **RPC 命令主题 `$rpc/#`** 只允许受信任的控制端发布；若违规发布，服务端通过 `UNKNOWN_METHOD` 错误响应静默拒绝。
4. **遗嘱消息 Retain=true** 须在设备恢复正常时发布一条空负载或 `connected` 状态覆盖，避免 Broker 上遗留历史遗嘱误导新订阅者。
5. **生产环境开启 TLS + 服务器证书验证**（`AllowUntrustedCertificates = false`），防止中间人攻击。
6. **定期轮换 MQTT 密码**，避免长期使用默认凭据 `001/001`。

---

> **维护说明**：新增 RPC 方法或事件类型时，同步更新本文档对应表格。事件类型常量定义于 `WebAPI/Models/StateChangeEvents.cs`，新增 eventType 后须同步更新该常量类与本文档 §2.3.1。
