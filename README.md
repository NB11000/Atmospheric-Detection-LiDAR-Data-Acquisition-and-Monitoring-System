# 大气检测激光雷达 — 数据采集与检测系统

> 双进程高频数据采集与分发系统，通过 MQTT 向远程终端实时推送波形、检测告警与设备状态。

## 项目概述

本系统驱动 USB1602 采集卡以 **1 MHz 采样率**采集双通道大气激光雷达回波信号，经 LiDAR 反演（能见度 Vis + 折射率结构常数 Cn²）后，通过 **MQTT 三层主题**将波形数据（100 ms/帧）、低频快照（7 s）、检测告警和设备状态实时推送至远程前端。

系统由两个进程组成：

| 进程 | 角色 | 运行时 |
|------|------|--------|
| **WebAPI**（主控进程） | ASP.NET Core 8.0，托管 MQTT 客户端、gRPC 服务端、共享内存管理、数据分发服务 | .NET 8 Self-Contained |
| **ConsoleApp1**（数据采集子进程） | 驱动 USB1602 采集卡，运行 5 线程数据流水线，完成 LiDAR 反演与检测 | .NET 8 Native AOT |

## 快速开始

### 硬件要求

- USB1602 数据采集卡 + 驱动 (`USB1602.dll`)
- 串口激光器（可选，不接激光器也能验证数据通道）
- EMQX MQTT Broker（云端 Serverless 或本地部署）

### 方式一：干跑模式（不依赖硬件和 Broker）

干跑模式通过 `MMFWriter` 向 CoreDataBus 注入模拟数据，验证主控进程的数据消费链路。

```bash
# 1. 构建解决方案
cd "数据采集与检测系统V2.0"
dotnet build

# 2. 仅启动 WebAPI 主控进程（不启动子进程）
cd WebAPI
dotnet run

# 3. 另开终端，用 MMFWriter 注入模拟数据
cd MMFWriter
dotnet run CoreDataBus 100000
```

此时主控进程从 CoreDataBus 读取模拟数据并通过 MQTT 发布（需配置 Broker 连接）。可在 EMQX Dashboard 观察主题流量。

### 方式二：完整硬件模式

```bash
# 1. 安装 USB1602 驱动
#    确保 USB1602.dll 已复制到 ConsoleApp1 输出目录

# 2. 配置设备参数
#    编辑 WebAPI/Config/DeviceConfig.json：
#      - CaptureCard: 采集卡参数（采样率、时钟源、触发源、量程）
#      - LidarAlgorithm: LiDAR 反演参数（增益系数、孔径、路径长度等）
#      - Radar: 激光器串口参数
#      - Persistence: CSV 输出目录

# 3. 配置 MQTT Broker 连接
#    编辑 WebAPI/appsettings.json：
#      - BrokerHost / BrokerPort: EMQX 地址
#      - Username / Password: MQTT 认证凭据
#      - MachineId: 本机唯一标识（多机部署时区分设备）

# 4. 放置 TLS 证书
#    将 EMQX CA 证书保存为项目根目录的 emqxsl-ca.crt
#    （已在 appsettings.json 中配置路径）

# 5. 编译启动
dotnet build
cd WebAPI
dotnet run

# 系统将自动：
#   - 创建 CoreDataBus + UISharedBuffer 共享内存
#   - 启动 ConsoleApp1 数据采集子进程
#   - 连接 MQTT Broker
#   - 等待远程命令（通过 MQTT RPC 或 REST API）
```

## 部署指南

### 1. 前置依赖

| 依赖 | 版本要求 | 说明 |
|------|---------|------|
| .NET SDK | 8.0+ | 构建和运行 |
| EMQX | 5.x（Serverless 或自建） | MQTT Broker，需 TLS 1.2+ |
| TLS 证书 | CA 根证书 | EMQX Serverless 提供下载 |
| USB1602 采集卡 | 硬件 | 含 Windows 驱动 `USB1602.dll` |
| 串口激光器 | RS-232 | 可选 |

### 2. MQTT Broker 配置

**EMQX Serverless（推荐）：**
1. 在 [EMQX Cloud](https://www.emqx.com/zh/cloud) 创建 Serverless 实例
2. 下载 CA 根证书 → 保存为 `emqxsl-ca.crt`，放到项目根目录
3. 创建用户名/密码认证凭据 → 填入 `appsettings.json`

**本地 EMQX（可选）：**
```bash
docker run -d --name emqx \
  -p 1883:1883 -p 8883:8883 -p 8083:8083 -p 18083:18083 \
  emqx/emqx:latest
```

### 3. 配置文件

**`WebAPI/appsettings.json` — MQTT 连接：**
```json
{
  "Mqtt": {
    "BrokerHost": "z0d131fe.ala.cn-hangzhou.emqxsl.cn",
    "BrokerPort": 8883,
    "MachineId": "daq-srv-01",
    "Username": "001",
    "Password": "001",
    "UseTls": true,
    "AllowUntrustedCertificates": false,
    "CaCertificatePath": "E:\\...\\emqxsl-ca.crt",
    "RpcTimeoutSeconds": 60,
    "ReconnectDelaySeconds": 5,
    "WaveformPublishIntervalMs": 100
  }
}
```

**`WebAPI/Config/DeviceConfig.json` — 设备与算法参数：**
```json
{
  "CaptureCard": {
    "DeviceId": 0,
    "SyncChannelIndex": 2,
    "SampleRate": 1000,
    "ClockSourceIndex": 0,
    "HalfFullThreshold": 5,
    "TriggerSourceIndex": 1,
    "RangeIndex": 0
  },
  "LidarAlgorithm": {
    "GainEqualizationCoefficient": 1.0,
    "KConstant": 4.48,
    "ReceiverApertureD_m": 0.2,
    "PathLengthL_m": 1000.0,
    "Cn2WindowFrames": 100,
    "LaserWavelength_nm": 532.0
  },
  "Radar": {
    "LaserPower": 0,
    "LaserModulationFrequency": 0,
    "SerialPort": "COM3",
    "BaudRate": 9600
  },
  "Persistence": {
    "DataDirectory": "data"
  }
}
```

### 4. 发布部署

**主控进程（WebAPI）：**
```bash
dotnet publish WebAPI -c Release -r win-x64 --self-contained
```

**数据采集子进程（ConsoleApp1）：**
```bash
dotnet publish ConsoleApp1 -c Release -r win-x64 --self-contained
```

将两个发布目录复制到目标机器，先启动 WebAPI，WebAPI 会自动拉起 ConsoleApp1。

### 5. 验证部署

```bash
# 检查系统状态
curl http://localhost:5135/api/system/state

# 检查采集子进程连接
curl http://localhost:5135/api/collector/command/status

# 在 EMQX Dashboard 观察主题流量
# 订阅 daq/+/waveform/# 查看波形推送
```

## 架构说明

### 进程拓扑

```
┌─────────────────────────────────────────────────────────┐
│                    MQTT Broker (EMQX)                     │
│              z0d131fe.ala.cn-hangzhou.emqxsl.cn           │
└──────┬──────────────────────────────────┬────────────────┘
       │ MQTTS (TLS 1.2+)                 │ MQTTS
       │ QoS 0/1                          │
┌──────▼──────────────────┐      ┌───────▼─────────────────┐
│   WebAPI 主控进程        │      │   远程监控前端 / 仪表盘   │
│   ASP.NET Core 8.0      │      │   (MQTT 订阅者)          │
│   端口: 5135 (REST)      │      └─────────────────────────┘
│         ≥10000 (gRPC)   │
└──────┬──────────────────┘
       │ gRPC 双向流 (HTTP/2)
       │ 命令 / 状态 / 检测告警
┌──────▼──────────────────┐
│ ConsoleApp1 数据采集子进程 │
│ .NET 8 Native AOT        │
│ 驱动 USB1602 + 串口激光器  │
└──────┬──────────────────┘
       │
       ├── CoreDataBus (MMF, ~96 MB)
       │   StructuredSample[1M] 环形缓冲区
       │   单写 (Analysis) / 多读 (持久化, 低频发布)
       │
       └── UISharedBuffer (MMF, ~480 KB)
           double[30000]×2 环形缓冲区
           单写 (UI线程) / 单读 (波形发布)
```

**进程间通信：**
- **gRPC 双向流**：主控 → 子进程（命令下发），子进程 → 主控（状态上报 / 检测告警）
- **CoreDataBus（MMF）**：子进程 Analysis 线程逐条写入结构化采样点，主控进程消费者按周期读取
- **UISharedBuffer（MMF）**：子进程 UI 线程降采样写入波形，主控进程 100ms 周期读取并发布 MQTT

### 子进程内部线程拓扑

```
                          ┌─────────┐
                          │ ADWork  │
                          │ 采集线程  │
                          └────┬────┘
                               │ Voltage_block
                               ▼
                          ┌─────────┐
                          │ ADDraw  │
                          │ 预处理   │
                          └────┬────┘
                               │
               ┌───────────────┴───────────────┐
               │ Voltage_block                 │ Voltage_block
               │ (原始电压，同引用)              │ (原始电压，同引用)
               ▼                               ▼
        ┌──────────┐                    ┌──────────┐
        │ Analysis │                    │ UI 线程   │
        │ 分析线程  │                    │降采样1000:1│
        └────┬─────┘                    └────┬─────┘
             │                               │
     ┌───────┼───────┐                       ▼
     │逐条流式 │整批写入 │               ┌──────────────┐
     ▼       ▼       │               │UISharedBuffer│
┌─────────┐ ┌──────────┐ │               │   (MMF)      │
│CoreData │ │Detection │ │               └──────┬───────┘
│  Bus    │ │ Channel  │ │                      │
│ (MMF)   │ │ (同进程)  │ │                      ▼
└────┬────┘ └────┬─────┘ │               ┌──────────────┐
     │           │       │               │WaveformPublish│
     │           ▼       │               │  (主控进程)    │
     │    ┌──────────┐   │               │ 100ms → MQTT │
     │    │Detection │   │               └──────────────┘
     │    │ 检测线程  │   │
     │    └────┬─────┘   │
     │         │         │
     │         ▼         │
     │    gRPC 双向流 →   │
     │    主控进程        │
     │    (Detection     │
     │    Publisher)     │
     │                   │
     ▼                   │
┌─────────────────────┐  │
│ PersistenceService  │  │
│  持久化服务 (主控进程) │  │
└─────────────────────┘  │
┌─────────────────────┐  │
│ LowFrequencyPublisher│  │
│  低频发布 (主控进程)   │  │
└─────────────────────┘  │
```

**5 条线程职责：**

| 线程 | 输入 | 输出 | 说明 |
|------|------|------|------|
| **ADWork** | USB1602 采集卡 | `Voltage_block`（CH1/CH2 原始电压） | 采样率 1 MHz，双通道 16-bit ADC |
| **ADDraw** | `Voltage_block` | `Voltage_block`（分流至 Analysis + UI，同引用免拷贝） | 预处理占位（数据校验、帧同步） |
| **Analysis** | `Voltage_block`（来自 ADDraw） | `StructuredSample` → CoreDataBus（逐条）/ DetectionChannel（整批） | LiDAR 反演（Vis + Cn²），暗电流扣除，距离平方校正，增益均衡 |
| **Detection** | `DetectionBatch`（Channel） | 检测告警 → gRPC → 主控进程 | 信号遮挡 / 工况异常 / 跳变检测 |
| **UI** | `Voltage_block`（来自 ADDraw，原始电压） | UISharedBuffer 写入（降采样 1000:1） | 对原始电压降采样，供主控进程波形发布 |

### 数据流层 — StructuredSample

`StructuredSample`（96 字节）是系统内部唯一的数据货币，流经 CoreDataBus 和 DetectionChannel：

```
┌──────────────────────────────────────────────────────────────┐
│ 字段           │ 类型      │ 字节  │ 说明                     │
├──────────────────────────────────────────────────────────────┤
│ Timestamp      │ long      │ 8     │ session-local 递增序号    │
│ Time           │ long      │ 8     │ Stopwatch ticks，供 UTC 还原 │
│ CH1            │ double    │ 8     │ 通道 1 电压值 (V)         │
│ CH2            │ double    │ 8     │ 通道 2 电压值 (V)         │
│ Vis            │ double    │ 8     │ 能见度 (km)，整帧共享      │
│ Cn2            │ double    │ 8     │ 折射率结构常数 (m⁻²/³)    │
│ Temp           │ double    │ 8     │ 温度 (℃)                 │
│ Humi           │ double    │ 8     │ 相对湿度 (%)              │
│ Press          │ double    │ 8     │ 大气压力 (hPa)            │
│ WindSpd        │ double    │ 8     │ 风速 (m/s)                │
│ Rain           │ double    │ 8     │ 雨量 (mm)                 │
│ WindDir        │ double    │ 8     │ 风向 (°)                  │
├──────────────────────────────────────────────────────────────┤
│ 合计           │           │ 96    │                          │
└──────────────────────────────────────────────────────────────┘
```

**CoreDataBus 关键属性：**

| 属性 | 值 | 说明 |
|------|-----|------|
| 结构 | 扁平环形数组 `StructuredSample[1_000_000]` | ~96 MB，单写多读，lock-free |
| WriteIndex | `long` 单调递增，永不回绕 | 取模得物理位置，消费者无需追踪回绕 |
| 写入方式 | 逐条流式（每填充完 1 条立即 `Write()`） | 消费者无需等待整帧 |
| 内存屏障 | `MemoryBarrier`（写数据 → 屏障 → 推进 WriteIndex） | 消费者 `Volatile.Read` 配对，保证 happens-before |
| 时间校准 | `CoreBusHeader.ReferenceTick + ReferenceUtcTicks` | 消费者据此还原绝对 UTC 时间 |

## 项目结构

```
数据采集与检测系统V2.0/
├── WebAPI/                          ← 主控进程 (ASP.NET Core 8.0)
│   ├── Program.cs                   │  入口点 + DI 注册 + 子进程启动
│   ├── appsettings.json             │  MQTT Broker 连接配置
│   ├── Config/                      │  配置模型 + DeviceConfig.json
│   ├── Controllers/                 │  REST API（Laser / Collector / System / Log）
│   ├── Service/                     │  核心服务层
│   │   ├── MqttRpcBackgroundService.cs  │  托管 MQTT 客户端，RPC 路由
│   │   ├── GrpcServiceImpl.cs       │  gRPC 双向流服务端
│   │   ├── AcquisitionLifecycleCoordinator.cs │ 采集绑定服务生命周期协调
│   │   ├── WaveformPublishService.cs│  高频波形发布 (100ms)
│   │   ├── LowFrequencyPublisher.cs │  低频快照发布 (7s)
│   │   ├── PersistenceService.cs    │  周期性 CSV 持久化
│   │   ├── DetectionPublisherService.cs │ 检测告警发布
│   │   ├── MqttEventPublisher.cs    │  状态变更事件 / 设备报警发布
│   │   ├── SystemStateService.cs    │  系统状态中心（事件驱动）
│   │   └── CniLaser.cs              │  激光器串口控制
│   ├── MqttRpc/                     │  RPC 处理器（按模块路由）
│   ├── Models/                      │  DTO + 配置模型
│   └── Protos/Grpc.proto            │  gRPC 服务定义
│
├── ConsoleApp1/                     ← 数据采集子进程 (.NET 8 Native AOT)
│   ├── Program.cs                   │  入口点 + DI + 父进程监控
│   ├── Service/
│   │   ├── AD_Controlcs.cs          │  5 线程采集控制器（核心）
│   │   ├── LidarInverter.cs         │  LiDAR 反演算法（Vis + Cn²）
│   │   ├── GrpcClient.cs            │  gRPC 双向流客户端
│   │   └── SharedMemoryClient.cs    │  CoreDataBus + UISharedBuffer (MMF)
│   ├── Models/
│   │   ├── StructuredSample.cs      │  96 字节核心数据结构
│   │   ├── Voltage_block.cs         │  原始电压数据块
│   │   └── DetectionBatch.cs        │  检测批次包装（ArrayPool）
│   ├── Tools/
│   │   └── USB1602.cs               │  采集卡 Native Interop
│   └── USB1602.dll                  │  采集卡驱动（复制到输出目录）
│
├── WebAPI.Tests/                    ← xUnit 集成测试 + 单元测试
├── MMFWriter/                       ← 测试工具（向 CoreDataBus 注入模拟数据）
├── docs/adr/                        ← 架构决策记录
├── CONTEXT.md                       ← 领域术语表
├── MQTT主题文档.md                   ← MQTT 主题完整参考（Payload / QoS / 安全）
├── WebAPI接口文档.md                 ← REST API 参考
├── 高频数据采集与分发系统软件架构说明文档.md ← 架构详细说明
└── 数据采集与检测系统V2.0.sln         ← 解决方案文件
```

## 技术栈

### WebAPI（主控进程）

| 包 | 版本 | 用途 |
|----|------|------|
| .NET | 8.0 | 运行时 |
| MQTTnet | 5.1.0 | MQTT 客户端 + RPC 扩展 |
| Grpc.AspNetCore | 2.76.0 | gRPC 服务端 |
| Serilog.AspNetCore | 10.0.0 | 结构化日志 |
| Swashbuckle | 6.6.2 | Swagger UI |
| System.IO.Ports | 10.0.5 | 串口通信（激光器控制） |

### ConsoleApp1（数据采集子进程）

| 包 | 版本 | 用途 |
|----|------|------|
| .NET | 8.0 Native AOT | 运行时（自包含，无依赖） |
| Grpc.Net.Client | 2.76.0 | gRPC 客户端 |
| Google.Protobuf | 3.34.0 | Protobuf 序列化 |
| NetMQ | 4.0.2 | ZeroMQ（保留依赖） |
| System.IO.Ports | 10.0.5 | 串口通信 |

## MQTT 主题速查

### 数据上报

| 主题 | QoS | Retain | 间隔 | Payload | 说明 |
|------|-----|--------|------|---------|------|
| `daq/{id}/waveform/ch1` | 0 | 否 | 100ms | 二进制 `double[1000]` | 通道 1 降采样波形 |
| `daq/{id}/waveform/ch2` | 0 | 否 | 100ms | 二进制 `double[1000]` | 通道 2 降采样波形 |
| `daq/{id}/lowfreq` | 1 | 否 | 7s | JSON（12 字段） | 低频采样快照 |
| `daq/{id}/detection/alerts` | 1 | 否 | 事件驱动 | JSON | 检测告警 |

### 状态与事件

| 主题 | QoS | Retain | 说明 |
|------|-----|--------|------|
| `daq/{id}/events/state_changed` | 1 | 否 | 状态变更推送 |
| `daq/{id}/events/will` | 1 | **是** | 遗嘱消息（进程崩溃时 Broker 自动发布） |
| `daq/{id}/events/device_alarm` | 1 | **是** | 设备报警 |
| `daq/{id}/events/data_updated` | 0 | 否 | 数据更新事件（待启用） |

### 命令下发（RPC）

请求主题模板：`$rpc/{id}/{方法名}/{关联ID}`，响应追加 `/response`。详细方法列表见 `MQTT主题文档.md`。

**常用 RPC 方法：**

| 方法名 | 功能 | 响应类型 |
|--------|------|---------|
| `system-state` | 查询系统状态 | `SystemStateDto` |
| `collector-open-device` | 打开采集卡 | `CommandResult` |
| `collector-start-ad` | 开始采集 | `CommandResult` |
| `collector-stop-ad` | 停止采集 | `CommandResult` |
| `collector-close-device` | 关闭采集卡 | `CommandResult` |
| `laser-on` | 开启激光 | `CommandResult` |
| `laser-off` | 关闭激光 | `CommandResult` |

## 文档索引

| 文档 | 说明 |
|------|------|
| [CONTEXT.md](CONTEXT.md) | 领域术语表、架构决策、LiDAR 反演定义 |
| [MQTT主题文档.md](MQTT主题文档.md) | 完整 MQTT 主题参考（Payload 结构 / QoS / 安全建议） |
| [WebAPI接口文档.md](WebAPI接口文档.md) | REST API 参考（Laser / Collector / System / Log） |
| [docs/adr/](docs/adr/) | 架构决策记录 |
| [高频数据采集与分发系统软件架构说明文档.md](高频数据采集与分发系统软件架构说明文档.md) | 架构详细说明 |
| [前后端交互逻辑-强乐观模式.md](前后端交互逻辑-强乐观模式.md) | 前后端交互逻辑 |
| [提交记录/](提交记录/) | 历次提交记录 |
| [系统优化文档/](系统优化文档/) | 系统优化记录 |

## 项目约定

- **采集绑定服务的生命周期**由 `AcquisitionLifecycleCoordinator` 统一管理，根据采集状态和 MQTT 连接状态自动启停
- **采集绑定服务不继承 `BackgroundService`**，而是纯 Singleton，各自管理内部 CTS 和 Dispose
- **CoreDataBus WriteIndex** 永不回绕，用 `long` 单调递增
- **持久化 = 周期性快照抽样**，非全量归档
- **Cn² 前 99 帧输出 -1.0**（哨兵值，语义 = 无效），消费者需自行跳过
- **波形主题使用二进制 Payload**（非 JSON），16 KB/帧/通道（1000 个 double），禁止 JSON 解析
