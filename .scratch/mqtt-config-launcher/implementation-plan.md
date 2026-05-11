# Implementation Plan: MQTT 配置启动器与本地控制面板

> Parent: [PRD: MQTT 配置启动器与本地控制面板](../PRD.md)

## Modules

| 模块 | 类型 | 职责 |
|------|------|------|
| `SharedModels` | 共享类库 | MqttSettings / CommandResult / SystemStateDto — WebAPI 和启动器共同引用 |
| `ConfigManager` | 纯逻辑类 | 读写 `appsettings.json` Mqtt 节点 + Launcher 节点；读写 `.mqtt_configured` 标记文件；暴露 `HasExistingConfig`, `LoadConfig`, `SaveConfig`, `MarkConfigured`, `LoadBaseUrl`, `SaveBaseUrl` |
| `WebApiProcessManager` | 基础设施类 | `Start()` 拉起 WebAPI.exe；`StopAsync()` 调 `api/system/shutdown` 优雅退出 → 超时 Kill；`WaitUntilReadyAsync()` HTTP 健康检查轮询 |
| `LauncherHttpClient` | 基础设施类 | 封装 `HttpClient` 调用 WebAPI 控制器：采集卡指令、激光器指令、系统状态查询、shutdown。返回 SharedModels 强类型 DTO |
| `ConfigViewModel` | ViewModel | 配置页 UI 逻辑：表单验证、高级字段折叠、保存→启动→切换至控制页 流程编排 |
| `ControlViewModel` | ViewModel | 控制页 UI 逻辑：采集卡/激光器按钮绑定、状态快照展示、手动刷新触发 |
| `MainWindowViewModel` | ViewModel | 窗口级逻辑：标签切换（配置/控制/日志）、WebAPI 就绪检测把控、页面状态机 |
| `MainWindow` | View | 单窗口 + TabControl 三页布局、底部状态栏（BaseUrl 输入 + WebAPI 连接状态指示器） |

## Interfaces

### ConfigManager

```
class ConfigManager(baseDirectory: string):
    HasExistingConfig() → bool                    // .mqtt_configured 是否存在
    LoadConfig() → MqttSettings                  // 读取 appsettings.json → Mqtt 节点；文件不存在则返回默认 MqttSettings
    SaveConfig(MqttSettings) → void              // 仅写入/替换 Mqtt 节点，保留 Logging/AllowedHosts/Launcher 等节点；文件不存在则生成完整 JSON（含 Logging/AllowedHosts 默认值）
    MarkConfigured() → void                       // 创建 .mqtt_configured 空文件
    LoadBaseUrl() → string                        // 读 Launcher.BaseUrl 节点，不存在返回 "http://localhost:5135"
    SaveBaseUrl(string) → void                    // 写 Launcher.BaseUrl 节点
```

### WebApiProcessManager

```
class WebApiProcessManager(webApiDirectory: string):
    Start() → Process                             // Process.Start("WebAPI.exe")，工作目录 = webApiDirectory
    StopAsync(baseUrl, timeoutSec) → Task<bool>   // POST api/system/shutdown → WaitForExit → 超时 Kill
    WaitUntilReadyAsync(baseUrl, timeoutSec) → Task<bool>  // GET {baseUrl}/ 轮询直到 200 OK
```

### LauncherHttpClient

```
class LauncherHttpClient(baseUrl: string):
    Task<SystemStateDto> GetSystemState()
    Task<CommandResult> OpenDevice()
    Task<CommandResult> CloseDevice()
    Task<CommandResult> StartAcquisition()
    Task<CommandResult> StopAcquisition()
    Task<CommandResult> LaserConnect()
    Task<CommandResult> LaserDisconnect()
    Task<CommandResult> LaserOn()
    Task<CommandResult> LaserOff()
    Task<bool> ShutdownWebApi()
```

### MqttSettings（已有，不改）

```
class MqttSettings:  // appsettings.json → Mqtt 节点
    BrokerHost, BrokerPort, MachineId, Username,
    Password, UseTls, AllowUntrustedCertificates,
    CaCertificatePath, RpcTimeoutSeconds,
    ReconnectDelaySeconds, WaveformPublishIntervalMs
```

### SharedModels（WebAPI 与启动器共同引用）

SharedModels 为独立 .NET 类库，包含：

```
MqttSettings:            // BrokerHost, BrokerPort, MachineId, Username, Password,
                         // UseTls, AllowUntrustedCertificates, CaCertificatePath,
                         // RpcTimeoutSeconds, ReconnectDelaySeconds, WaveformPublishIntervalMs

CommandResult:           // Success: bool, Code: string, Message: string,
                         // State: SystemStateDto?, Timestamp: DateTime

SystemStateDto:          // Server, Collector, Laser, UiHints, Timestamp
```

WebAPI 项目将现有模型移至 SharedModels 并添加项目引用；启动器引用 SharedModels 获取相同类型。

## Data Flow

### 首次配置 → 启动 → 控制

```
用户双击 Launcher.exe
  → ConfigManager.HasExistingConfig() → false
  → MainWindow 展示 ConfigPage（空白表单，MachineId 预填主机名）
  → 用户填写字段，点击"保存并启动"
  → ConfigViewModel 校验字段 → ConfigManager.SaveConfig(settings)
  → ConfigManager.MarkConfigured() → 写 .mqtt_configured
  → WebApiProcessManager.Start() → Process.Start("WebAPI.exe")
  → WebApiProcessManager.WaitUntilReadyAsync("http://localhost:5135", 30s)
      → 成功: MainWindow.切换到 ControlPage
      → 超时: 弹错误框，用户可重试
```

### 已有配置 → 启动 → 控制

```
用户双击 Launcher.exe
  → ConfigManager.HasExistingConfig() → true
  → LauncherHttpClient.GetSystemState() 检测 5135 是否可达
      → 可达: 直接进入 ControlPage（连上已有 WebAPI 实例）
      → 不可达: 展示配置摘要 + 三个按钮
          → "使用已有配置启动": Start→WaitUntilReady→ControlPage
          → "修改配置": 表单预填当前值→保存→询问重启→ControlPage
          → "退出": Application.Exit
```

### 控制面板操作

```
用户在 ControlPage 点击"打开设备"
  → ControlViewModel → LauncherHttpClient.OpenDevice()
  → POST api/collector/command/open
  → WebAPI → gRPC → 子进程 → 返回 CommandResult
  → ControlViewModel 展示结果（成功/失败 + message）
  → 用户点击"刷新状态"
  → ControlViewModel → LauncherHttpClient.GetSystemState()
  → 更新状态快照展示
```

### 修改 MQTT 配置并重启

```
用户进入 ConfigPage → 修改表单 → 点击保存
  → ConfigManager.SaveConfig(新配置)
  → 弹框"配置已保存，WebAPI 需重启生效，是否立即重启？"
  → 确认
      → ConfigViewModel → WebApiProcessManager.StopAsync(baseUrl, 5s)
          → LauncherHttpClient.ShutdownWebApi() → POST api/system/shutdown
          → WaitForExit(5s) → 成功
          → 超时 → Process.Kill()
      → WebApiProcessManager.Start()
      → WebApiProcessManager.WaitUntilReadyAsync(baseUrl, 30s)
      → ControlPage
  → 取消: 仅保存，下次重启生效
```

## Key Technical Decisions

| # | 决策 | 理由 |
|---|------|------|
| 1 | **ConfigManager 只读写 Mqtt 节点** | 保留 Logging/AllowedHosts 等其他配置不被覆盖 |
| 2 | **`.mqtt_configured` 标记文件而非检查 BrokerHost 默认值** | 当前 appsettings.json 的 BrokerHost 已是真实地址，无法作为"未配置"依据 |
| 3 | **启动器不引入 DI 容器** | 项目仅 3 个 ViewModel + 3 个服务类，DI 的复杂度超过收益 |
| 4 | **HTTP 轮询检测就绪而非 Pipe 通信** | HTTP 是最低耦合方案，启动器无需知道 WebAPI 内部状态 |
| 5 | **MachineId 代码默认值保持 `daq-srv-01`** | 向后兼容，表单预填 `Environment.MachineName` 覆盖 |
| 6 | **控制面板使用手动刷新** | 简化首版，后续可优化为事件驱动 |
| 7 | **启动器关闭不影响 WebAPI** | 用户误关启动器不会导致采集中断 |
| 8 | **WebAPI 新增 `api/system/shutdown` 端点** | 原有 `api/collector/command/exit` 退出的是子进程非 WebAPI；需独立端点通过 IHostApplicationLifetime 关闭主控进程 |
| 9 | **SharedModels 类库共享 DTO** | 避免模型拷贝版本发散；WebAPI 将现有模型移入 SharedModels，启动器直接引用 |
| 10 | **BaseUrl 持久化至 appsettings.json → Launcher 节点** | 与 Mqtt 节点平级，ConfigManager 隔离读写 |

## Test Strategy

| 模块 | 测试类型 | 焦点 | 不测试的内容 |
|------|---------|------|-------------|
| ConfigManager | 单元测试（xUnit） | `SaveConfig` 写入后 `LoadConfig` 可正确回读；`MarkConfigured` 后 `HasExistingConfig` 返回 true；不存在的文件 `HasExistingConfig` 返回 false；写入空 BrokerHost 时抛异常 | — |
| WebApiProcessManager | 不做单元测试 | — | 进程启动依赖外部 exe 和 HTTP 就绪，纯集成行为 |
| LauncherHttpClient | 不做单元测试 | — | 依赖 WebAPI 运行实例 |
| ViewModels | 不做单元测试 | — | 依赖 UI 框架绑定和外部服务 |

## Vertical Slice Design

```
Slice 1 (基础设施) → Slice 2 (配置表单) → Slice 3 (进程管理) → Slice 4 (控制面板)
     无前置              依赖 Slice 1          依赖 Slice 1+2           依赖 Slice 1+2+3
```

### Slice 1: 项目骨架 + ConfigManager
- 依赖: 无
- 产出: Avalonia 项目创建 → ConfigManager 类 → 单元测试通过
- 验证: 独立运行 ConfigManager，读写 appsettings.json Mqtt 节点正常

### Slice 2: 配置窗口（首次配置 + 已有配置）
- 依赖: Slice 1
- 产出: MainWindow + ConfigPage + ConfigViewModel（表单/摘要/三按钮/高级折叠）
- 验证: 填写表单 → 保存 → 文件落盘；.mqtt_configured 创建后第二次打开看到摘要视图

### Slice 3: WebAPI 进程管理
- 依赖: Slice 1, Slice 2
- 产出: WebApiProcessManager + ConfigViewModel 中集成 Start/Stop/WaitUntilReady
- 验证: 点击"保存并启动" → WebAPI 控制台窗口出现 → 启动器切换到 ControlPage

### Slice 4: 本地控制面板
- 依赖: Slice 1, Slice 2, Slice 3
- 产出: LauncherHttpClient + ControlViewModel + ControlPage + 重启流程
- 验证: 全部指令按钮可下发 → 状态手动刷新正常 → 修改配置后重启流程闭环
