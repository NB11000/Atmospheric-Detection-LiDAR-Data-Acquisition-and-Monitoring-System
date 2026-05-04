# Web API 接口文档

## 项目概述

本项目为数据采集与检测系统的Web API服务，提供激光器控制、数据采集子进程命令转发、系统状态查询和日志管理等功能。API采用RESTful设计风格，返回统一格式的JSON响应。

## 基础信息

- **基础URL**: `http://localhost:5135` (HTTP/1.1) 或动态端口 (HTTP/2)
- **统一响应格式**: 大多数操作命令返回 `CommandResult` 对象
- **跨域支持**: 已配置允许所有来源的CORS策略

## 控制器列表

1. **LaserController** - 激光器控制接口
2. **ClientController** - 数据采集子进程命令转发接口
3. **SystemStateController** - 系统状态查询接口
4. **LogController** - 系统日志管理接口

---

## 1. LaserController - 激光器控制

**基础路径**: `api/laser`

### 1.1 连接激光器串口

- **方法**: `POST`
- **路径**: `/api/laser/connect`
- **描述**: 打开串口连接激光器，从全局配置中获取串口号和波特率
- **请求参数**: 无
- **响应格式**: `CommandResult`

```json
{
  "success": true,
  "code": "LASER_CONNECTED",
  "message": "串口连接成功",
  "state": { /* SystemStateDto */ },
  "timestamp": "2024-01-01T00:00:00"
}
```

**状态码**:
- `200`: 成功（无论连接成功与否，都返回200，通过success字段判断）
- 错误时code可能为: `LASER_CONNECT_FAILED`

### 1.2 断开激光器串口

- **方法**: `POST`
- **路径**: `/api/laser/disconnect`
- **描述**: 断开串口连接
- **请求参数**: 无
- **响应格式**: `CommandResult`

```json
{
  "success": true,
  "code": "LASER_DISCONNECTED",
  "message": "串口已断开连接",
  "state": { /* SystemStateDto */ },
  "timestamp": "2024-01-01T00:00:00"
}
```

### 1.3 开启激光

- **方法**: `POST`
- **路径**: `/api/laser/on`
- **描述**: 开启激光，从全局配置中获取激光功率和调制频率并设置
- **请求参数**: 无
- **响应格式**: `CommandResult`

```json
{
  "success": true,
  "code": "LASER_ON",
  "message": "激光开启成功",
  "state": { /* SystemStateDto */ },
  "timestamp": "2024-01-01T00:00:00"
}
```

**状态码**:
- `200`: 成功或失败（通过success字段判断）
- 失败时code可能为: `LASER_ON_FAILED`

### 1.4 关闭激光

- **方法**: `POST`
- **路径**: `/api/laser/off`
- **描述**: 关闭激光
- **请求参数**: 无
- **响应格式**: `CommandResult`

```json
{
  "success": true,
  "code": "LASER_OFF",
  "message": "激光已关闭",
  "state": { /* SystemStateDto */ },
  "timestamp": "2024-01-01T00:00:00"
}
```

### 1.5 获取激光器状态

- **方法**: `GET`
- **路径**: `/api/laser/status`
- **描述**: 检查激光器连接状态
- **请求参数**: 无
- **响应格式**: 匿名对象

```json
{
  "connected": true,
  "emissionOn": false,
  "portName": "COM3",
  "timestamp": "2024-01-01T00:00:00"
}
```

**状态码**:
- `200`: 成功
- `500`: 内部服务器错误

### 1.6 更新激光雷达配置

- **方法**: `POST`
- **路径**: `/api/laser/config/update`
- **描述**: 更新激光雷达配置并保存到配置文件，更新内存中的全局配置
- **请求参数**: `RadarConfig` JSON对象
- **响应格式**: `RadarConfig` (更新后的配置)

```json
{
  "laserPower": 100,
  "laserModulationFrequency": 1000,
  "serialPort": "COM3",
  "baudRate": 9600
}
```

**状态码**:
- `200`: 成功
- `400`: 配置不能为空
- `500`: 内部服务器错误

### 1.7 读取激光雷达配置

- **方法**: `POST`
- **路径**: `/api/laser/config/read`
- **描述**: 读取配置文件中的激光雷达配置并更新全局配置实体
- **请求参数**: 无
- **响应格式**: `RadarConfig` (最新的全局配置)

```json
{
  "laserPower": 100,
  "laserModulationFrequency": 1000,
  "serialPort": "COM3",
  "baudRate": 9600
}
```

---

## 2. ClientController - 数据采集子进程命令转发

**基础路径**: `api/collector/command`

**注意**: 客户端ID硬编码为"数据采集子进程"

### 2.1 检查数据采集子进程连接状态

- **方法**: `GET`
- **路径**: `/api/collector/command/status`
- **描述**: 检查数据采集子进程是否已连接
- **请求参数**: 无
- **响应格式**: 连接状态对象

```json
{
  "clientId": "数据采集子进程",
  "connected": true,
  "timestamp": "2024-01-01T00:00:00"
}
```

**状态码**:
- `200`: 成功
- `500`: 内部服务器错误

### 2.2 发送指令（同步）

- **方法**: `POST`
- **路径**: `/api/collector/command`
- **描述**: 向数据采集子进程发送指令并等待响应
- **请求参数**: 指令内容字符串
- **请求体示例**: `"OPEN_DEVICE"`
- **响应格式**: gRPC响应对象（具体结构由gRPC服务定义）

**状态码**:
- `200`: 成功
- `400`: 指令不能为空
- `404`: 数据采集子进程未连接
- `504`: 等待客户端响应超时
- `500`: 内部服务器错误

### 2.3 发送指令（异步）

- **方法**: `POST`
- **路径**: `/api/collector/command/async`
- **描述**: 向数据采集子进程发送指令（异步，不等待响应）
- **请求参数**: 指令内容字符串
- **请求体示例**: `"START_AD"`
- **响应格式**: 无内容

**状态码**:
- `202`: 已接受（异步处理）
- `400`: 指令不能为空
- `404`: 数据采集子进程未连接
- `500`: 内部服务器错误

### 2.4 打开采集卡设备

- **方法**: `POST`
- **路径**: `/api/collector/command/open`
- **描述**: 发送打开采集卡设备指令
- **请求参数**: 无
- **响应格式**: `CommandResult`

```json
{
  "success": true,
  "code": "COLLECTOR_OPENED",
  "message": "设备打开成功",
  "state": { /* SystemStateDto */ },
  "timestamp": "2024-01-01T00:00:00"
}
```

### 2.5 重新打开采集卡设备

- **方法**: `POST`
- **路径**: `/api/collector/command/open-again`
- **描述**: 发送重新打开采集卡设备指令
- **请求参数**: 无
- **响应格式**: `CommandResult`

```json
{
  "success": true,
  "code": "COLLECTOR_OPENED",
  "message": "设备重新打开成功",
  "state": { /* SystemStateDto */ },
  "timestamp": "2024-01-01T00:00:00"
}
```

### 2.6 关闭采集卡设备

- **方法**: `POST`
- **路径**: `/api/collector/command/close`
- **描述**: 发送关闭采集卡设备指令
- **请求参数**: 无
- **响应格式**: `CommandResult`

```json
{
  "success": true,
  "code": "COLLECTOR_CLOSED",
  "message": "设备关闭成功",
  "state": { /* SystemStateDto */ },
  "timestamp": "2024-01-01T00:00:00"
}
```

### 2.7 开始采集

- **方法**: `POST`
- **路径**: `/api/collector/command/start`
- **描述**: 发送开始采集指令
- **请求参数**: 无
- **响应格式**: `CommandResult`

```json
{
  "success": true,
  "code": "AD_STARTED",
  "message": "采集开始成功",
  "state": { /* SystemStateDto */ },
  "timestamp": "2024-01-01T00:00:00"
}
```

### 2.8 停止采集

- **方法**: `POST`
- **路径**: `/api/collector/command/stop`
- **描述**: 发送停止采集指令
- **请求参数**: 无
- **响应格式**: `CommandResult`

```json
{
  "success": true,
  "code": "AD_STOPPED",
  "message": "采集停止成功",
  "state": { /* SystemStateDto */ },
  "timestamp": "2024-01-01T00:00:00"
}
```

### 2.9 心跳检测

- **方法**: `POST`
- **路径**: `/api/collector/command/ping`
- **描述**: 发送心跳检测指令
- **请求参数**: 无
- **响应格式**: gRPC响应对象

### 2.10 优雅退出

- **方法**: `POST`
- **路径**: `/api/collector/command/exit`
- **描述**: 发送优雅退出指令
- **请求参数**: 无
- **响应格式**: gRPC响应对象

### 2.11 读取采集卡配置

- **方法**: `POST`
- **路径**: `/api/collector/command/config/read`
- **描述**: 发送读取配置指令，读取配置文件更新全局配置实体
- **请求参数**: 无
- **响应格式**: `CaptureCardConfig` (全局配置)

```json
{
  "deviceId": 0,
  "syncChannelIndex": 2,
  "sampleRate": 1000,
  "clockSourceIndex": 0,
  "halfFullThreshold": 5,
  "triggerSourceIndex": 1,
  "rangeIndex": 0,
  "channel": "通道1和通道2",
  "clockSource": "内时钟",
  "halfFullThresho": "64K",
  "triggerSource": "软触发",
  "range": "±5V"
}
```

### 2.12 更新采集卡配置

- **方法**: `POST`
- **路径**: `/api/collector/command/config/update`
- **描述**: 更新采集卡配置，同时通知数据采集子进程配置已更新
- **请求参数**: `CaptureCardConfig` JSON对象
- **响应格式**: `CaptureCardConfig` (更新后的全局配置)

**状态码**:
- `200`: 成功
- `400`: 配置不能为空
- `500`: 内部服务器错误

### 2.13 获取默认采集卡配置

- **方法**: `GET`
- **路径**: `/api/collector/command/config/default`
- **描述**: 获取默认采集卡配置（不更新全局配置实体和配置文件）
- **请求参数**: 无
- **响应格式**: `CaptureCardConfig` (默认配置)

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

---

## 3. SystemStateController - 系统状态查询

**基础路径**: `api/system`

### 3.1 获取系统统一状态快照

- **方法**: `GET`
- **路径**: `/api/system/state`
- **描述**: 获取系统统一状态快照
- **请求参数**: 无
- **响应格式**: `SystemStateDto`

```json
{
  "server": {
    "isApiAlive": true,
    "timestamp": "2024-01-01T00:00:00"
  },
  "collector": {
    "processConnected": true,
    "deviceOpened": false,
    "acquiring": false,
    "handle": 0,
    "lastMessage": "",
    "timestamp": "2024-01-01T00:00:00"
  },
  "laser": {
    "serialConnected": false,
    "emissionOn": false,
    "portName": "",
    "lastMessage": "",
    "timestamp": "2024-01-01T00:00:00"
  },
  "uiHints": {
    "canOpenCollector": true,
    "canCloseCollector": false,
    "canStartAcquisition": false,
    "canStopAcquisition": false,
    "canConnectLaser": true,
    "canDisconnectLaser": false,
    "canTurnLaserOn": false,
    "canTurnLaserOff": false
  },
  "timestamp": "2024-01-01T00:00:00"
}
```

**状态码**:
- `200`: 成功
- `500`: 内部服务器错误

---

## 4. LogController - 系统日志管理

**基础路径**: `api/logs`

### 4.1 获取系统日志（支持分页、过滤、时间范围）

- **方法**: `GET`
- **路径**: `/api/logs`
- **描述**: 获取系统日志，支持分页、级别过滤、时间范围查询
- **查询参数**:
  - `limit`: 返回日志条数限制（默认100，最大1000）
  - `offset`: 跳过前N条日志（默认0）
  - `level`: 日志级别过滤（如Information、Error）
  - `from`: 起始时间（ISO 8601格式，如2024-01-01T00:00:00）
  - `to`: 结束时间（ISO 8601格式）
- **响应格式**: 分页日志结果

```json
{
  "total": 150,
  "limit": 100,
  "offset": 0,
  "count": 100,
  "logs": [
    {
      "timestamp": "2024-01-01T00:00:00+08:00",
      "level": "Information",
      "message": "系统启动完成",
      "exception": null,
      "properties": {
        "SourceContext": "WebAPI.Controllers.LogController"
      }
    }
  ]
}
```

**状态码**:
- `200`: 成功
- `500`: 内部服务器错误

### 4.2 按日志级别获取日志

- **方法**: `GET`
- **路径**: `/api/logs/{level}`
- **描述**: 按指定日志级别获取日志
- **路径参数**:
  - `level`: 日志级别（如Information、Error、Warning）
- **查询参数**:
  - `limit`: 返回日志条数限制（默认100）
- **响应格式**: 指定级别的日志列表

```json
{
  "level": "Information",
  "count": 50,
  "logs": [
    {
      "timestamp": "2024-01-01T00:00:00+08:00",
      "level": "Information",
      "message": "系统启动完成",
      "exception": null,
      "properties": {}
    }
  ]
}
```

**状态码**:
- `200`: 成功
- `400`: 无效的日志级别
- `500`: 内部服务器错误

### 4.3 获取日志级别统计信息

- **方法**: `GET`
- **路径**: `/api/logs/levels`
- **描述**: 获取所有日志级别的统计信息
- **请求参数**: 无
- **响应格式**: 统计信息

```json
{
  "totalLogs": 150,
  "statistics": [
    {
      "level": "Information",
      "count": 120,
      "latest": "2024-01-01T00:00:00"
    },
    {
      "level": "Warning",
      "count": 20,
      "latest": "2024-01-01T00:00:00"
    }
  ]
}
```

### 4.4 清空内存中的日志

- **方法**: `DELETE`
- **路径**: `/api/logs`
- **描述**: 清空内存中的日志（功能暂未完全实现）
- **请求参数**: 无
- **响应格式**: 操作结果

```json
{
  "message": "内存日志已清空"
}
```

**注意**: 此功能目前仅返回成功消息，实际未清空日志

### 4.5 健康检查

- **方法**: `GET`
- **路径**: `/api/logs/health`
- **描述**: 健康检查端点
- **请求参数**: 无
- **响应格式**: 状态消息

```json
{
  "status": "OK",
  "message": "LogController is working"
}
```

---

## 响应模型定义

### CommandResult - 统一命令响应模型

```json
{
  "success": true,
  "code": "COLLECTOR_OPENED",
  "message": "设备打开成功",
  "state": { /* SystemStateDto */ },
  "timestamp": "2024-01-01T00:00:00"
}
```

**字段说明**:
- `success`: 命令是否成功
- `code`: 命令结果码，如 COLLECTOR_OPENED, AD_STARTED
- `message`: 给UI展示的消息
- `state`: 命令执行后的最新系统状态
- `timestamp`: 响应时间戳

### SystemStateDto - 系统统一状态快照

**结构层次**:
1. `server`: 服务器状态
2. `collector`: 采集卡状态
3. `laser`: 激光器状态
4. `uiHints`: UI可操作提示状态
5. `timestamp`: 快照生成时间

### RadarConfig - 雷达配置类

```json
{
  "laserPower": 100,
  "laserModulationFrequency": 1000,
  "serialPort": "COM3",
  "baudRate": 9600
}
```

### CaptureCardConfig - 采集卡配置类

```json
{
  "deviceId": 0,
  "syncChannelIndex": 2,
  "sampleRate": 1000,
  "clockSourceIndex": 0,
  "halfFullThreshold": 5,
  "triggerSourceIndex": 1,
  "rangeIndex": 0,
  "channel": "通道1和通道2",
  "clockSource": "内时钟",
  "halfFullThresho": "64K",
  "triggerSource": "软触发",
  "range": "±5V"
}
```

### LogEntryDto - 日志条目数据传输对象

```json
{
  "timestamp": "2024-01-01T00:00:00+08:00",
  "level": "Information",
  "message": "系统启动完成",
  "exception": null,
  "properties": {
    "SourceContext": "WebAPI.Controllers.LogController"
  }
}
```

---

## HTTP状态码说明

| 状态码 | 说明 | 常见场景 |
|--------|------|----------|
| 200 | 成功 | 大多数请求成功返回 |
| 202 | 已接受 | 异步命令已接受处理 |
| 400 | 请求错误 | 参数验证失败、请求体为空 |
| 404 | 未找到 | 数据采集子进程未连接 |
| 500 | 内部服务器错误 | 服务器端异常 |
| 504 | 网关超时 | 等待客户端响应超时 |

**注意**: 部分接口即使操作失败也返回200状态码，需通过响应体中的`success`字段判断操作结果。

---

## 使用示例

### 获取系统状态
```bash
curl -X GET http://localhost:5135/api/system/state
```

### 连接激光器
```bash
curl -X POST http://localhost:5135/api/laser/connect
```

### 打开采集卡设备
```bash
curl -X POST http://localhost:5135/api/collector/command/open
```

### 查询日志
```bash
curl -X GET "http://localhost:5135/api/logs?limit=10&level=Information"
```

---

**文档生成时间**: 2026-04-19  
**项目版本**: 数据采集与检测系统V2.0



目前的整个项目是这样的，我把原先的w e b a p i项目改造成一个主控进程，这个主控进程将取消原先w e b a p i中的所有web能力只做，一个主控进程 用于托管mqtt客户端,文件系统,以及其他各种各样的系统状态服务。

现在我想让你使用MQTTnet.Extensions.Rpc，来接管webapi中的控制器中的所有逻辑，给我一个计划


不删除w e b a p i的主要代码，及保留asp net core框架的能力，让mqtt客户端托管在asp net core框架之上。 


方可继续 现在开始实施计划注意。 每完成1步都要暂停由我来审核审核通过。 再继续。 


目前对于这个后台服务进行专门的优化,我发现你在很多地方都是自己手动实现的，而并没有使用mqtt net强大的能力，比如发送响应模式自动重连心跳检测等这些功能。并且我发现你的代码中有很多地方容易触发gc，请找出你这个后台服务的更多问题，并且我要求你充分使用m q t t net的本身的能力。 避免自己手动造轮子


你给我拿错查看这些文件当前的设计是不是自己造轮子了,这些代码逻辑能不能更换为m q t t net 封装好的等级而不是自己手动造轮子。 请审查一下。 并在聊天框中输出你的回答。 


进入这个目录中的git仓库 根据他的更改信息。 来为我生成1份git提交消息。 并且生成1份详细的提交记录要求 有明确的时间日期，输出成markdown文件，放在E:\新建文件夹 (2)\数据采集MQTT版\数据采集与检测系统V2.0\提交记录 

你的这个 第一段的陈述迁移w e b a p i控制器逻辑至m q t t r p c主通道架构。 你的这个信息写的太过 底层 能不能写一下这个信息的目的是什么，这次更改的目的是什么是为了解决什么样的问题 。在聊天框中输出这个答案。 

更新文件。 不不不你的这个精简信息，我认为不对。 这一次的更改。 是为了避免原先给每一个设备进行内网穿透导致的维护困难。 而是使用mqtt这种通信，来更好的与设备通信和连接。 


关于波形数据发布循环，我发现当前的逻辑是如果程序一启动连接上了mq t t服务器，那么就开始波形数据发布循环。 我想让这个发布循环在开始采集后才开始发布波形数据。 在聊天框中输出一个计划。 


我现在要求重新修改计划，我发现原先的计划不合理。 首先就是太过复杂了，在时序图中会发现跨越了好几层，才触发了波形定期发布。而且我目前发现波形定期发布与m q t t r p c后台服务是耦合在一起的，啊我们又在外部又重新编写了一个mqtt的事件发布服务。 为何不能让波形 数据的传输像一个普通的数据发布一样。 只不过，它的频率更高一些而已。 

请分析，这种方案是否可行 如果可行，则重新设计计划


注意,不存在此进程主动对mqtt的场景所有的数据都通过webapi主控进程推送到mqtt。我认为 WaveformPublishService 服务 应该并入到MqttEventPublisher中，你认为呢 


我未来不仅要上传，波形数据还有上传，其他的数据。 现在我有以下几个问题，那就是波形数据和其他数据的上传能否整合在同一个服务中。第2个问题就是，目前数据的发布与是否采集相关关联是否需要关联 

要求，要讲清楚。 背景问题是什么 解决方案是什么 清晰明了


当前的项目采集的是大气检测激光雷达的数据。现在我的数据采集子进程中的全量数据分析线程中缺少，将模拟值电压数据转化成实际的物理量的算法。目前，整个数据流中，只有将采集卡采集到的数字量转化成模拟电压值这一个路线。现在我想让你补齐从模拟电压值转换成实际物理量的链路。需要转化的实际物理量为Vis（Visibility，能见度）和Cn2​（大气折射率结构常数）



分析，目前数据采集子进程中的数据流，你会发现AD数据处理线程会将数据划分成2个分支。 一条分支流向u i调度线程一条分支流向全量数据分析线程。 这就导致了模拟值电压被拷贝成了两份流向不同的分支。最终2个分支又将各自的数据流向共享内存然后由主控进程读取并发送给mqtt服务器。 然而，需要注意的是流向ui调度线程的数据是降采样后的电压数据，而流向全量数据分析线程的数据是全量没有降采样后的电压数据。 这就导致在共享内存中储存的数据电压数据和全量分析线程分析后的物理量数据，他俩是不一致不相对应的。所以我的想法是 能否在a d数据处理线程中，不再划分2个分支而是直接将全量数据直接传送到全量数据分析线程全量数据分析线程分析好之后。 将电压数据物理量数据打包成一个结构体然后在统一发送给ui调度线程，这样直接变成了一条流水线不再有分支 同时，电压数据和物理量数据可以对齐。如何降采样你来分析 


重构数据采集子进程的数据流拓扑，移除AD数据处理线程中的双分支复制逻辑，改为单通道直通：AD数据处理线程将原始全量电压数据直接推送至全量数据分析线程，禁止在此阶段进行任何分叉或副本生成；全量数据分析线程完成特征提取与物理量计算后，将对齐的时间戳或序列标识、原始电压数据、计算所得物理量封装为不可拆分的统一结构体，经线程安全队列或环形缓冲区提交至UI调度线程；UI调度线程仅负责从该结构体中按需抽取并执行降采样，降采样策略采用时间窗或计数窗滑动平均/峰值保持/抽取滤波，确保输出频率与刷新周期匹配且不破坏与物理量的一致性；两线程间取消各自独立写入共享内存的路径，改由全量数据分析线程在结构体就绪后一次性写入共享内存，主控进程读取该单一来源并转发至MQTT服务器；同步引入序列号或单调递增ID与轻量级校验，消除生产-消费速率不匹配导致的对齐漂移，并明确缓冲区容量、背压丢弃策略与延迟上限以保障实时性。

根据这份文档中的模块设计，先完成3.1 数据采集与结构化模块与3.2 核心数据总线（环形缓冲区+内存映射文件）模块，先制定一个计划。关于3.2核心数据总线可以借鉴或复用SharedMemoryClient.cs


目前核心数据总线依然先采用扁平环形数组，如果是分槽块状结构，那么数据持久化服务和ui刷新服务，就不能获取实时数据前一段的数据。因为如果是分槽块状结构，那么，每一次 分析线程要立即到一个数据块才开始写入数据到数据总线，那么，那么就会导致写指针在分析线程写入下一个数据块之前一直停留在某处。这个时候数据持久化服务和u i刷新服务则会一直读取一个重复时间段的数据。也就是说，数据持久化服务和u i刷新服务对实时性的要求也是有一定的 


告诉我，你这份计划中，每一个消费者是如何读取数据的讲讲具体的细节 



现在有一个具体的场景，那就是数据持久化服务 会按照周期比如1秒5秒30秒1分钟，5分钟，这样的周期。来获取数据并写入到csv文件。我看到你的计划中写入的逻辑是分析线程在分析完一个数据块之后再将这个数据块批量写入数据总线。然后消费者们好像还批量读取了。但是我的设想是低频数据更新服务。是每7秒获取单条数据。 数据持久化服务是根据它的周期每次获取单条数据并写入到文件中。并且我需要时间的间隔是精确的比如说，隔1秒，那么上一条数据和此条数据的时间间隔就是一秒。你目前的计划符不符合这个要求？如果不符合请进行改进


不不你搞错了分析线程与 检测线程之间的通道中放入的应该是真实的数据。只不过分析线程将数据块分析出来的结构体数据。 分成2路一路进入数据总线一路进入与检测线程之间的通道。 


这样写 当数据在分析线程中分成2路的时候。 一路进入数据总线的这一路数据是逐个写入而不是批量写入。 进入检测线程通道的这一路数据是批量写入的 





现在我需要推翻目前所有的计划。 目前通过事件来驱动3个服务即数据持久化服务高频数据，发布服务低频数据发布服务。我想能否通过信号来驱动就像gstreams的信号总线。因为我发现，目前，整个过程，其实就是一个数据流水线由数据采集子进程获取并转化数据写入到共享内存中，然后主控进程读取并开始数据持久化低频和高频数据发布。现在开始评估是用信号驱动好，还是用事件启动好。 


状态还是回到原来的事件驱动吧。 我发现由信号驱动的话，对于跨进程场景中难度非常大，并且也并不合理，因 我的数据持久化服务和 低频数据发布服务以及高频数据发布服务，他们其实是从数据流水线中抽样取一些快照出来。而信号驱动是在一条流水线上它需要消费者一直获取数据并且不丢失数据。


E:\新建文件夹 (2)\数据采集MQTT版\数据采集与检测系统V2.0进入这个目录中的git仓库 根据他的更改信息。 来为我生成1份git提交消息。 并且生成1份详细的提交记录要求 有明确的时间日期，输出成markdown文件，放在E:\新建文件夹 (2)\数据采集MQTT版\数据采集与检测系统V2.0\提交记录。格式参考2026-05-05 工程基础设施初始化—测试框架搭建、Issue驱动工作流建立与工具函数提取提交记录.md。生成之后 并将提交报告暂存到仓库中，然后你自己写一份提交信息将暂存的所有文件自动提交到仓库中 


