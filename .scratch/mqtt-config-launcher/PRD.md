# MQTT 配置启动器与本地控制面板

- **Label**: needs-triage
- **Status**: Draft

## Problem Statement

非专业用户部署数据采集系统时必须直接编辑 `appsettings.json` 中的 MQTT 配置节点（Broker 地址、端口、用户名、密码、TLS 证书等）。这导致两个问题：(1) JSON 语法错误可导致程序启动失败；(2) 多台设备部署时 `MachineId` 重复导致 MQTT Broker 踢除旧连接（ClientID 碰撞），用户不理解原因。

同时，当前的远程控制依赖 MQTT 通道（远程前端 → MQTT RPC → WebAPI），若 MQTT Broker 或远程前端不可达，操作员无法对本地采集系统下达指令。

## Solution

新建 **AvaloniaApplication_ConfigLauncher** —— 跨平台桌面应用，提供：

1. **MQTT 配置 GUI**：表单式填写 MQTT 凭据，写入 `appsettings.json`，自动标记首次配置完成
2. **WebAPI 进程管理**：通过 `Process.Start` 拉起 WebAPI，`api/collector/command/exit` 优雅退出，超时强杀
3. **本地控制面板**：通过 WebAPI HTTP 接口操控采集卡和激光器，作为 MQTT 远程前端失效时的备用通道

## User Stories

1. As a 非专业部署人员, I want 打开桌面应用就能看到一个配置表单, so that 我不需要学习 JSON 语法就能完成系统部署
2. As a 部署人员, I want 配置表单预填了常见默认值（TLS 开启、端口 8883、MachineId 自动填主机名）, so that 我只需改最少字段就能完成配置
3. As a 部署人员, I want 填写完配置点击"保存并启动"后系统自动启动, so that 我不需要在文件资源管理器里找 exe 双击
4. As a 部署人员, I want 下次打开启动器时看到"使用已有配置启动 / 修改配置 / 退出"三个选项, so that 我不需要重复填写配置
5. As a 操作员, I want 在远程前端连不上时打开启动器进入本地控制面板, so that 我仍然可以控制采集卡的启停和激光器
6. As a 操作员, I want 本地控制面板上点击按钮下发指令（打开设备 / 开始采集 / 停止采集 / 激光开关）, so that 我不需要记住 gRPC 指令名称
7. As a 操作员, I want 手动刷新系统状态快照, so that 我确认指令是否生效
8. As a 运维人员, I want 修改 MQTT 配置后系统提示是否重启 WebAPI, so that 新配置能立即生效而不用我去任务管理器杀进程
9. As a 运维人员, I want 关闭启动器时不影响正在运行的 WebAPI, so that 采集不会因为我误关配置界面而中断
10. As a 高级用户, I want 展开高级设置面板配置 CA 证书路径和 TLS 详细选项, so that 我能适配不同安全策略的 MQTT Broker
11. As a 技术人员, I want 可以跳过启动器直接双击 WebAPI.exe 启动, so that 我的自动化脚本和 SSH 远程部署不受影响

## Implementation Decisions

### 模块划分

| 模块 | 位置 | 职责 |
|------|------|------|
| SharedModels | 新共享类库 | MqttSettings / CommandResult / SystemStateDto 等跨项目共用模型 |
| ConfigManager | 新项目 | 读写 appsettings.json 的 Mqtt 和 Launcher 节点 + .mqtt_configured 标记文件 |
| WebApiProcessManager | 新项目 | WebAPI 进程生命周期：Start / Stop(graceful→kill) / IsReachable |
| LauncherHttpClient | 新项目 | 轻量 HTTP 封装，覆盖 collector/laser/state/shutdown 端点 |
| MainWindowViewModel | 新项目 | 单窗口标签切换（配置/控制/日志三页） |
| ConfigViewModel | 新项目 | 首次配置表单 + 已有配置摘要 + 启动/修改/退出逻辑 |
| ControlViewModel | 新项目 | 本地控制面板：采集卡/激光器指令 + 状态快照 + 手动刷新 |

### 架构决策

- 启动器仅通过 HTTP 与 WebAPI 通信（`http://localhost:5135`），不依赖 MMF / gRPC / MQTT
- 启动器不引入 DI 容器 / Serilog —— 异常用 MessageBox 弹窗
- 启动器关闭不影响 WebAPI 运行（关闭前弹确认框）
- WebAPI 就绪检测：轮询 `http://localhost:5135`（1s 间隔，最多 30s 超时）
- `.mqtt_configured` 空标记文件判断首次运行 vs 已有配置
- 修改 MQTT 配置后：调用 `api/system/shutdown` 优雅退出 WebAPI → 5s 超时强杀 → 重新拉起
- WebAPI 新增 `POST api/system/shutdown` 端点（IHostApplicationLifetime.StopApplication()）
- SharedModels 类库供 WebAPI 和启动器共同引用，避免模型拷贝和版本发散

### API 契约

启动器调用 WebAPI 的 HTTP 端点：
- `GET api/system/state` — 系统状态快照
- `POST api/collector/command/open` / `close` / `start` / `stop` — 采集卡控制
- `POST api/system/shutdown` — 优雅退出 WebAPI（**新增**，注入 IHostApplicationLifetime.StopApplication()）
- `POST api/laser/connect` / `disconnect` / `on` / `off` — 激光器控制

### 启动器自身配置持久化

- BaseUrl 存入 `appsettings.json` → `Launcher.BaseUrl` 节点（与 `Mqtt` 节点平级，隔离读写）
- ConfigManager.LoadBaseUrl() / SaveBaseUrl(url) 负责该节点
- Mqtt 节点的读写不触碰 Launcher 节点

### 配置字段分级

**核心字段（始终可见）：** BrokerHost, BrokerPort, MachineId, Username, Password, UseTls
**高级字段（折叠面板隐藏）：** AllowUntrustedCertificates, CaCertificatePath, RpcTimeoutSeconds, ReconnectDelaySeconds, WaveformPublishIntervalMs

## Testing Decisions

- **ConfigManager** 是唯一可独立测试的深模块 —— 测试 JSON 读写和标记文件逻辑
- 其余模块（ViewModel / HttpClient / ProcessManager）依赖外部进程或 UI 框架，不做单元测试
- 测试原则：通过公共接口验证行为，不耦合实现细节

## Out of Scope

- 启动器内嵌 WebAPI 控制台输出
- 控制面板自动轮询/事件驱动状态刷新（首版手动刷新）
- 跨平台打包（macOS / Linux 构建配置）
- WebAPI 命令行配置向导
- SignalR 状态推送集成

## Further Notes

- WebAPI 的 `MqttSettings.MachineId` 默认值保持 `"daq-srv-01"` 不变（便于老用户升级兼容）
- 启动器 GUI 表单中 MachineId 预填 `Environment.MachineName`
- 废弃的 AvaloniaApplication1 项目不动
