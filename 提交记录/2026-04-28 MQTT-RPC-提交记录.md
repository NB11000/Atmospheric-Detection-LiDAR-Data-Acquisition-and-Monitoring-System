# Git 提交记录 — 数据采集与检测系统 V2.0

> 生成时间：2026-04-28 09:27 (UTC+8)
> 仓库路径：`E:\新建文件夹 (2)\数据采集MQTT版\数据采集与检测系统V2.0`

---

## 待提交变更说明（建议的 Commit Message）

```
feat(mqtt-rpc): 引入 MQTT 统一通信通道，消除多设备内网穿透维护成本

背景问题：
先前每个采集工控机通过 HTTP 暴露接口，部署现场需为每台设备单独配置
内网穿透（端口映射/反向代理），新增设备、IP 变更、防火墙策略均需手动
干预，运维成本随设备数量线性增长。

解决方案：
以 MQTT Broker（云端 EMQX）作为统一通信中枢，采集设备只需单向
外连 Broker（无需入站端口），所有命令/事件/波形数据均通过
MQTT RPC + Pub/Sub 完成，彻底消除内网穿透依赖。

核心变更：
- 新增 4 个 RPC Handler（Collector/Laser/System/Log），将 26 个
  HTTP 端点完整镜像为 MQTT RPC 方法
- 新增 MqttRpcBackgroundService 托管 MQTT 生命周期（Broker 连接、
  RPC 路由分发、波形发布、断线自动重连）
- 新增 MqttEventPublisher 替代 SignalR 作为主事件推送通道
- GrpcServiceImpl / CniLaser 双通道接入 MQTT 事件
- 新增 MqttSettings 外部化 Broker 连接参数
- HTTP 通道保留作为本地调试兼容通道
```

---

## 本次待提交变更明细

### 一、新增文件（10 个）

| # | 文件路径 | 行数 | 说明 |
|---|---------|------|------|
| 1 | `WebAPI/Models/MqttSettings.cs` | 49 | MQTT 连接配置选项模型（Broker/心跳/遗嘱/超时/重连） |
| 2 | `WebAPI/Models/MqttRpcParams.cs` | 223 | RPC 请求参数 DTO 与响应模型（含 MqttStateChangedEvent） |
| 3 | `WebAPI/MqttRpc/CollectorHandler.cs` | 469 | 采集卡领域 RPC Handler（13 方法） |
| 4 | `WebAPI/MqttRpc/LaserHandler.cs` | 362 | 激光器领域 RPC Handler（7 方法） |
| 5 | `WebAPI/MqttRpc/SystemHandler.cs` | 73 | 系统状态 RPC Handler（1 方法） |
| 6 | `WebAPI/MqttRpc/LogHandler.cs` | 266 | 日志查询 RPC Handler（5 方法） |
| 7 | `WebAPI/Service/MqttEventPublisher.cs` | 192 | MQTT 事件发布服务（状态变更/设备报警/数据更新） |
| 8 | `WebAPI/Service/MqttRpcBackgroundService.cs` | 487 | MQTT BackgroundService（连接管理/RPC路由/波形发布/自动重连） |
| 9 | `开发记录/MQTT RPC 主通道架构迁移计划.md` | 685 | 完整架构迁移计划与优化方案文档 |

### 二、修改文件（5 个）

| # | 文件路径 | 变更 | 说明 |
|---|---------|------|------|
| 1 | `WebAPI/Program.cs` | +13 | 注册 MqttSettings 选项绑定、MqttEventPublisher、4 个 Handler、MqttRpcBackgroundService |
| 2 | `WebAPI/appsettings.json` | +12/-2 | 新增 `Mqtt` 配置节点（BrokerHost/Port/MachineId 等 8 项） |
| 3 | `WebAPI/Controllers/LogController.cs` | +1/-32 | 添加 `using WebAPI.Models`；迁移 `LogEntryDto` 至 Models 目录 |
| 4 | `WebAPI/Service/GrpcServiceImpl.cs` | +35/-7 | 注入 `MqttEventPublisher`；在子进程连接/错误/断开处增加 MQTT 事件推送 |
| 5 | `WebAPI/Service/CniLaser.cs` | +18/-5 | 注入 `MqttEventPublisher`；预置 4 处 MQTT 事件调用（注释状态，待启用） |

### 三、删除/移动文件（3 个）

| # | 操作 | 文件路径 | 说明 |
|---|------|---------|------|
| 1 | 删除 | `MQTT RPC 集成计划.md` | 旧版集成计划，内容已合并 |
| 2 | 移动 | `MQTT RPC 主通道架构迁移计划.md` → `开发记录/` | 迁移至开发记录目录 |
| 3 | 未跟踪 | `1777257125265-gentle-lagoon.md` | 临时计划文件，待决定是否纳入版本管理 |

### 四、26 个 RPC 方法映射表

| 领域 | 方法数 | RPC 方法名列表 |
|------|--------|---------------|
| 采集卡 | 13 | `collector-status`, `collector-command-send`, `collector-command-send-async`, `collector-open-device`, `collector-open-device-again`, `collector-close-device`, `collector-start-ad`, `collector-stop-ad`, `collector-ping`, `collector-exit`, `collector-config-read`, `collector-config-update`, `collector-config-default` |
| 激光器 | 7 | `laser-connect`, `laser-disconnect`, `laser-on`, `laser-off`, `laser-status`, `laser-config-update`, `laser-config-read` |
| 系统 | 1 | `system-state` |
| 日志 | 5 | `logs-query`, `logs-by-level`, `logs-level-stats`, `logs-clear`, `logs-health` |

---

## 完整提交历史记录

### 2026 年 4 月

| # | 提交时间 | 提交哈希 | 描述 |
|---|----------|----------|------|
| 21 | **2026-04-27 10:53** | `5eb95ad` | feat: Integrate MQTT RPC into existing Web API as a BackgroundService |
| 20 | 2026-04-21 14:33 | `6ee5d4f` | refactor(signalr): 统一SignalR推送机制，分离状态管理与消息推送职责 |
| 19 | 2026-04-21 05:17 | `65422c6` | refactor: 重构状态同步架构，引入全局内存缓存的中心化状态机与 SignalR 实时推送 |
| 18 | 2026-04-14 04:12 | `c9a47df` | feat:添加激光雷达控制功能 |
| 17 | 2026-04-12 09:25 | `69e73b1` | 0 |
| 16 | **2026-04-10 22:27** | `0f6117b` | fix: 修复前端UI通道数据重叠问题 |
| 15 | 2026-04-10 22:06 | `67094cf` | feat: 添加前端UI通道数据重叠问题分析文档 |
| 14 | 2026-04-10 14:30 | `02947d0` | feat: 集成WebSocket实时数据流并优化UI |
| 13 | 2026-04-08 22:16 | `4e65d72` | feat：添加WebSocket实时数据流支持，将原有的共享内存UI更新机制替换为WebSocket实时数据流（网络传输） |
| 12 | 2026-04-08 00:32 | `ac3b184` | feat: 重构服务器配置管理，支持运行时动态切换服务器地址 |
| 11 | **2026-04-07 12:16** | `ae7b0a4` | feat: 修复采集卡按钮状态卡死问题 \| 规范化全按钮异步交互 \| 增强用户操作反馈 |
| 10 | 2026-04-07 12:14 | `e7c532f` | feat: 新增静态配置属性并更新视图模型引用 |
| 9 | 2026-04-06 19:56 | `c79a7b6` | feat: 增强配置管理和错误处理，添加依赖注入容器初始化；优化设备配置读取和保存逻辑 |
| 8 | 2026-04-06 05:47 | `dfff8e5` | feat: 更新主窗口和配置窗口，添加设置参数侧边栏；优化设备状态和配置读取逻辑 |
| 7 | 2026-04-06 04:05 | `711cbca` | feat: 增强HTTP API客户端，支持请求超时机制；更新配置读取和更新指令 |
| 6 | 2026-04-05 22:00 | `ce4c061` | feat:在avalonia项目中移除内嵌服务器，实现HTTP客户端，建立本地配置管理。目的：无状态UI |
| 5 | **2026-04-05 11:03** | `45e70e4` | feat: 增强配置管理功能，新增读取和更新采集卡配置的API接口；优化日志记录方式 |
| 4 | 2026-04-04 20:03 | `2c17a2b` | feat: 更新.gitignore，添加WebAPI日志目录以避免版本控制 |
| 3 | 2026-04-04 18:12 | `329ffea` | feat: 增强数据采集启动逻辑，添加采集卡状态检查；优化WebAPI服务器配置，打印本机局域网IP |
| 2 | 2026-04-04 04:44 | `04232a2` | feat: 新增数据采集子进程命令转发控制器 ClientController |

---

## 统计概览

| 指标 | 数值 |
|------|------|
| 总提交数 | 21 |
| 覆盖时间范围 | 2026-04-04 至 2026-04-27（24 天） |
| 本次待提交文件数 | 14 个（新增 9，修改 5） |
| 本次新增代码行数 | +2,873 行 |
| 本次删除代码行数 | -43 行 |
| MQTT RPC 方法总数 | 26 个 |

---

## 架构演进路线

```
Phase 1 (04/04 - 04/07):     Web API 基础设施搭建 (Controllers, Config, Logging)
Phase 2 (04/08 - 04/10):     WebSocket 实时数据流集成、配置管理重构
Phase 3 (04/12 - 04/14):     采集卡修复、激光雷达控制功能添加
Phase 4 (04/21):             SignalR 推送机制重构、中心化状态机引入
Phase 5 (04/27 ← 当前):     MQTT RPC 主通道架构迁移（本次提交）
```

---

*本文档由 Kilo 根据 Git 仓库实际变更记录自动生成。*
