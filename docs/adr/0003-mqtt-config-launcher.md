# ADR 0003: MQTT 配置启动器 — 双入口启动架构

## Date

2026-05-11

## Status

Accepted

## Context

MqttSettings（Broker 地址、端口、用户名、密码、TLS 等）当前硬编码于 `appsettings.json`。非专业用户直接编辑 JSON 门槛高。同时需要避免多机部署下 ClientID 碰撞（同 MachineId 导致 Broker 踢除旧连接）。

启动器的定位经历两次迭代：

1. **初始方案**：启动器是一次性配置工具，配完拉起 WebAPI 后退出
2. **最终方案**：启动器配完 MQTT 后不退出，进入本地控制面板——因为 WebAPI 提供 HTTP API（api/collector/*、api/laser/*、api/system/state），可作为远程前端失效时的备用本地控制通道

## Decision

系统启用**双启动入口**：

| 入口 | 目标用户 | 流程 |
|------|---------|------|
| `AvaloniaApplication_ConfigLauncher.exe` | 非专业用户 | GUI 配置 → 保存 → 自动拉起 WebAPI → 进入本地控制面板 |
| `WebAPI.exe`（直接启动） | 技术人员 | 直接读 appsettings.json，无命令行交互 |

启动器关键行为：

- **首次运行**（`.mqtt_configured` 不存在）：展示 MQTT 配置表单（BrokerHost / BrokerPort / MachineId / Username / Password / UseTls，高级字段折叠隐藏）
- **已有配置**（`.mqtt_configured` 存在 + WebAPI 未运行）：展示配置摘要 + 三个按钮（使用已有配置启动 / 修改配置 / 退出）
- **WebAPI 已运行**（HTTP 可达）：直接进入本地控制面板
- **修改 MQTT 配置后**：提示重启 WebAPI，优雅退出 → 超时强杀 → 重新拉起

## Consequences

### Pros
- 非专业用户不需要触碰 JSON 文件
- 本地控制面板作为远程 MQTT 前端的备用通道，提高系统可维护性
- ClientID 默认值预填 `Environment.MachineName`，降低多机碰撞风险
- 启动器仅通过 HTTP 与 WebAPI 通信，无 MMF/gRPC/MQTT 依赖，保持轻量

### Cons
- 项目新增一个 Avalonia 跨平台项目，增加构建复杂度
- 锁死在 Windows 构建：Avalonia 跨平台需额外配置 macOS/Linux 打包
- 控制面板为手动刷新（非事件驱动），首版不追求实时性

## Alternatives Considered

- **WebAPI 内置命令行配置问答**：与直接编辑 JSON 无本质差异，拒绝
- **启动器拉起 WebAPI 后退出（方案 A）**：浪费 WebAPI 自带的 HTTP 控制能力，拒绝
- **启动器嵌入 WebAPI 控制台输出（方案 B）**：引入进程间管道通信和 UI 复杂度，拒绝
