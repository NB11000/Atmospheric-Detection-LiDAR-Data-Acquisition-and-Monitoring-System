# 数据采集与检测系统 V2.0

## 项目概述

数据采集与检测系统 V2.0 是一个基于 .NET 8.0 和 Avalonia UI 框架开发的高性能数据采集与可视化系统。该系统采用双进程架构，实现了硬件数据采集、实时波形显示、数据存储与分析等功能。

### 核心特性

- **双进程架构**：分离 UI 进程与数据采集进程，提高系统稳定性和响应性
- **高性能数据流**：基于无锁环形缓冲区和内存映射文件实现高速数据传输
- **多协议通信**：支持 gRPC、ZeroMQ 和共享内存多种进程间通信方式
- **硬件支持**：集成 USB1602 数据采集卡驱动，支持双通道同步采集
- **实时可视化**：使用 OxyPlot 实现实时波形显示，支持缩放、平移等交互操作
- **配置化管理**：支持设备参数配置的持久化存储与动态加载

### 技术栈

- **开发平台**：.NET 8.0, C# 11
- **UI 框架**：Avalonia UI (跨平台桌面应用框架)
- **数据可视化**：OxyPlot.Avalonia
- **通信协议**：gRPC, ZeroMQ (NetMQ)
- **数据存储**：JSON 配置文件，内存映射文件
- **硬件接口**：USB1602.dll (数据采集卡驱动)

## 项目目录介绍

```
数据采集与检测系统V2.0/
├── AvaloniaApplication1/          # 主进程：UI 应用程序
│   ├── Assets/                   # 资源文件
│   ├── Config/                   # 配置文件类
│   │   └── CaptureCardConfig.cs  # 采集卡配置类
│   ├── Generated/                # 自动生成代码
│   │   └── Protos/              # gRPC 协议生成代码
│   ├── Models/                   # 数据模型
│   │   ├── Data_Block.cs        # 原始数据块结构
│   │   ├── UI_Display.cs        # UI 显示快照结构
│   │   └── Voltage_block.cs     # 电压数据块结构
│   ├── Process/                  # 数据处理模块
│   ├── Properties/               # 项目属性
│   ├── Protos/                   # gRPC 协议定义
│   │   └── Grpc.proto           # gRPC 协议文件
│   ├── Service/                  # 服务层
│   │   ├── GrpcServiceImpl.cs   # gRPC 服务实现
│   │   └── SharedMemoryServer.cs # 共享内存服务
│   ├── Tools/                    # 工具类
│   │   ├── ConfigHelper.cs      # 配置帮助类
│   │   ├── Tool.cs              # 通用工具
│   │   └── USB1602.cs           # USB1602 硬件接口
│   ├── ViewModels/              # 视图模型
│   │   ├── MainWindowViewModel.cs # 主窗口视图模型
│   │   └── ViewModelBase.cs     # 视图模型基类
│   ├── Views/                   # 视图层
│   │   ├── MainWindow.axaml     # 主窗口界面
│   │   └── Window1.axaml        # 子窗口界面
│   ├── App.axaml               # 应用程序入口
│   ├── Program.cs              # 程序主入口
│   └── AvaloniaApplication1.csproj # 项目文件
├── ConsoleApp1/                 # 子进程：数据采集控制台应用
│   ├── Config/                  # 配置文件类
│   ├── Generated/               # 自动生成代码
│   ├── Models/                  # 数据模型
│   ├── Properties/              # 项目属性
│   ├── Protos/                  # gRPC 协议定义
│   ├── Service/                 # 服务层
│   │   ├── AD_Controlcs.cs      # 数据采集控制类
│   │   ├── GrpcClient.cs        # gRPC 客户端
│   │   └── SharedMemoryClient.cs # 共享内存客户端
│   ├── Tools/                   # 工具类
│   ├── Program.cs               # 程序入口
│   ├── ZeroMQ.cs                # ZeroMQ 通信模块
│   └── ConsoleApp1.csproj       # 项目文件
├── packages/                    # NuGet 包目录
├── 数据采集与检测系统V2.0.sln   # Visual Studio 解决方案
├── .gitignore                  # Git 忽略文件
└── 问题解决记录.md             # 项目问题记录文档
```

## 具体每个项目中有类的功能

### AvaloniaApplication1 (UI 主进程)

#### 1. 核心类

**`Program.cs`** - 应用程序入口
- 初始化 Avalonia 应用程序
- 启动 gRPC 服务器
- 管理应用程序生命周期
- 提供全局日志记录器

**`MainWindowViewModel.cs`** - 主窗口视图模型
- 管理 UI 状态和数据绑定
- 处理用户交互命令（开始/停止采集等）
- 协调数据流与 UI 更新
- 管理设备配置和采集参数

**`GrpcServiceImpl.cs`** - gRPC 服务实现
- 处理与子进程的双向流式通信
- 管理客户端连接状态
- 发送采集命令并等待响应
- 实现命令-响应同步机制

#### 2. 数据模型类

**`CaptureCardConfig.cs`** - 采集卡配置
- 设备编号、采样频率、通道设置等参数
- JSON 序列化支持，用于配置文件持久化
- 采样周期计算和参数验证

**`Data_Block.cs`** - 原始数据块结构
- 存储原始字节数据缓冲区
- 记录数据长度和采样点信息
- 支持双通道/单通道采样模式

**`UI_Display.cs`** - UI 显示快照
- 双通道电压数据快照
- 用于实时波形显示的数据结构

**`Voltage_block.cs`** - 电压数据块
- 包含通道1和通道2的电压数据数组
- 采样点数量和时间戳信息

#### 3. 服务类

**`SharedMemoryServer.cs`** - 共享内存服务
- 基于内存映射文件的无锁环形缓冲区
- 实现 UI 数据的高性能传输
- 支持多生产者-单消费者模式
- 提供线程安全的数据读写接口

**`ConfigHelper.cs`** - 配置帮助类
- 读取和保存 JSON 配置文件
- 提供默认配置生成
- 配置验证和错误处理

#### 4. 工具类

**`USB1602.cs`** - 硬件接口封装
- USB1602 数据采集卡的 P/Invoke 封装
- 提供设备打开、关闭、读写等基础操作
- 硬件参数设置和状态查询

### ConsoleApp1 (数据采集子进程)

#### 1. 核心类

**`Program.cs`** - 控制台程序入口
- 解析命令行参数
- 初始化数据采集控制器
- 建立与主进程的通信连接
- 管理采集任务生命周期

**`AD_Controlcs.cs`** - 数据采集控制
- 核心采集逻辑实现
- 硬件设备控制和数据读取
- 数据预处理和格式转换
- 环形缓冲区管理和数据写入

**`ZeroMQ.cs`** - ZeroMQ 通信模块
- 实现与主进程的命令/响应通信
- 心跳检测和连接状态管理
- 命令解析和执行分发

#### 2. 客户端类

**`GrpcClient.cs`** - gRPC 客户端
- 连接到主进程的 gRPC 服务器
- 处理双向流式通信
- 响应主进程命令并返回结果

**`SharedMemoryClient.cs`** - 共享内存客户端
- 写入 UI 显示数据到共享内存
- 管理数据缓冲区和索引
- 确保数据写入的线程安全性

#### 3. 工具类

**`ConfigHelper.cs`** - 配置管理
- 加载设备配置参数
- 参数验证和默认值处理
- 与主进程配置同步

## 通讯协议格式

### 1. gRPC 协议 (主进程 ↔ 子进程)

#### 协议定义文件：`Grpc.proto`

```protobuf
syntax = "proto3";
package ad_acquisition.v1;

// 主进程→子进程的请求消息
message AdRequest {
  string request_id = 1;      // 请求唯一ID
  string command = 2;         // 命令标识
  google.protobuf.Any params = 3; // 命令参数
}

// 子进程→主进程的响应消息
message AdResponse {
  string response_id = 1;     // 关联的请求ID
  string message_type = 2;    // 消息类型标识
  string content = 3;         // 文本内容
  string error_code = 4;      // 错误码标识
  google.protobuf.Any data = 5; // 扩展数据
  string processId = 6;       // 客户端进程ID
  int64 mHandle = 7;          // 设备句柄
}

// 服务定义
service GrpcService {
  // 双向流式RPC
  rpc Communicate (stream AdResponse) returns (stream AdRequest);
}
```

#### 命令类型

| 命令 | 说明 | 参数示例 |
|------|------|----------|
| `OPEN_DEVICE` | 打开设备 | `{"device_id": 0}` |
| `OPEN_DEVICE_AGAIN` | 重新打开设备 | `{"device_id": 0}` |
| `START_AD` | 开始采集 | `{"sample_rate": 1000}` |
| `STOP_AD` | 停止采集 | 无 |
| `PING` | 心跳检测 | 无 |
| `EXIT` | 退出进程 | 无 |

#### 响应类型

| 消息类型 | 说明 | 错误码 |
|----------|------|--------|
| `READY` | 设备就绪 | `NONE` |
| `PONG` | 心跳响应 | `NONE` |
| `ERROR` | 错误响应 | `DEVICE_OPEN_FAILED` 等 |
| `RESPONSE` | 命令响应 | `NONE` |
| `ACQ_STARTED` | 采集已开始 | `NONE` |
| `ACQ_STOPPED` | 采集已停止 | `NONE` |
| `UNKNOWN_COMMAND` | 未知命令 | `UNKNOWN` |

### 2. 共享内存协议 (数据流传输)

#### 内存布局结构

```plaintext
┌─────────────────────────────────────┐
│ 内存头结构 (Header)                  │
│ - 写索引 (WriteIndex)               │
│ - 读索引 (ReadIndex)                │
│ - 缓冲区大小 (BufferSize)           │
│ - 数据块大小 (DataBlockSize)        │
│ - 通道数量 (ChannelCount)           │
├─────────────────────────────────────┤
│ 通道0数据缓冲区                     │
│ - 双精度浮点数组 (double[])         │
│ - 长度 = BufferSize * DataBlockSize │
├─────────────────────────────────────┤
│ 通道1数据缓冲区                     │
│ - 双精度浮点数组 (double[])         │
│ - 长度 = BufferSize * DataBlockSize │
└─────────────────────────────────────┘
```

#### 数据格式

- **数据类型**：双精度浮点数 (double)
- **采样率**：可配置，默认 1kHz
- **通道数**：2（双通道同步采集）
- **缓冲区大小**：1000 个数据块
- **数据块大小**：1000 个采样点

### 3. ZeroMQ 协议 (备用通信)

#### 消息格式

```json
{
  "command": "START_AD",
  "params": {
    "sample_rate": 1000,
    "channel": 2
  },
  "timestamp": "2024-01-01T12:00:00Z"
}
```

#### 通信模式

- **地址格式**：`tcp://127.0.0.1:5555`
- **Socket 类型**：Dealer-Router 模式
- **超时设置**：心跳超时 5 秒
- **重连机制**：自动重连，最大重试 3 次

## 部署与运行

### 环境要求

- **操作系统**：Windows 10/11 (64位)
- **运行时**：.NET 8.0 Runtime
- **硬件**：USB1602 数据采集卡
- **驱动**：USB1602.dll 驱动程序

### 构建步骤

1. 克隆项目到本地
2. 使用 Visual Studio 2022 或更高版本打开解决方案
3. 还原 NuGet 包依赖
4. 构建解决方案
5. 确保 USB1602.dll 驱动程序位于系统 PATH 或应用程序目录

### 运行步骤

1. **启动主进程**：运行 `AvaloniaApplication1/bin/Debug/net8.0/AvaloniaApplication1.exe`
2. **启动子进程**：主进程会自动启动子进程，或手动运行 `ConsoleApp1/bin/Debug/net8.0/ConsoleApp1.exe`
3. **配置设备参数**：在主界面中设置采样频率、通道数等参数
4. **开始采集**：点击"开始采集"按钮，观察实时波形显示

### 注意事项

1. **硬件连接**：确保 USB1602 设备正确连接到计算机 USB 端口
2. **驱动程序**：安装正确的 USB1602 驱动程序
3. **权限要求**：共享内存需要适当的系统权限
4. **防火墙设置**：gRPC 通信可能需要配置防火墙规则

### 故障排除

1. **设备无法打开**：检查设备 ID 设置和驱动程序状态
2. **数据不显示**：检查共享内存名称和大小配置
3. **通信失败**：检查 gRPC 服务器地址和端口配置
4. **性能问题**：调整缓冲区大小和采样率参数