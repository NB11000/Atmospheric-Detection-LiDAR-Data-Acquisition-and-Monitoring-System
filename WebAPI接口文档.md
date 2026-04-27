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