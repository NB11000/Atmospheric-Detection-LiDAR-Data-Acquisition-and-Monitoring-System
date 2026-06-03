# 提交记录

> 生成时间：2026-06-03
> 仓库：数据采集与检测系统 V2.0
> 分支：`main`

---

## 一、背景（Background）

配置启动器（ConfigLauncher）当前的连接状态栏仅显示 WebAPI HTTP 可达性，不感知 MQTT Broker 连接状态。用户需要知道设备是否已成功接入 MQTT Broker（尤其在新部署诊断时），但现有 UI 无法区分"WebAPI 已启动但 MQTT 未连接"和"全链路正常"两种状态。

同时，BaseUrl 被硬编码在顶部栏且仅通过失焦事件保存，缺少在配置表单中编辑的能力，也不支持通过 ViewModel 双向传播变更。

### 问题（Problem）

#### 1. MQTT 连接状态不可见

用户打开启动器后只能看到 WebAPI 是否可达，无法判断 MQTT Broker 是否连接成功。部署人员需要额外打开浏览器查看仪表盘才能确认。

#### 2. BaseUrl 管理分散

BaseUrl 仅在 MainWindow 顶部栏可编辑（`LostFocus` 事件保存），ConfigView 中缺少入口。配置修改后 BaseUrl 变更无法通过 ViewModel 回调通知 MainWindow 刷新连接状态。

#### 3. WebAPI.csproj 循环引用

WebAPI 项目引用了 `AvaloniaApplication_ConfigLauncher.csproj`——但启动器是 WebAPI 的启动方（Process.Start），WebAPI 不依赖启动器的任何类型。此引用为历史遗留，可能引发构建歧义。

---

## 二、解决方案（Solution）

### 整体思路

在 `SystemStateDto.ServerStateDto` 中新增 `IsMqttConnected` 字段（由 `SystemStateService` 本地缓存维护），启动器通过 HTTP API 读取后渲染为独立的 MQTT 连接状态指示器。同时将 BaseUrl 纳入 ConfigViewModel 表单管理，通过 `NotifyBaseUrlChanged` 回调实现配置→状态栏的双向同步。

### 具体实施

#### 1. SystemStateDto 新增 IsMqttConnected 字段

`SharedModels/SystemStateDto.ServerStateDto` 和 `WebAPI/Models/SystemStateDto.ServerStateDto` 同步新增：

```csharp
public bool IsMqttConnected { get; set; }
```

WebAPI 端 `SystemStateService.GetSystemState()` 已在上一提交中注入 `_mqttConnected → IsMqttConnected`，本提交只需暴露 DTO 字段。

#### 2. 启动器状态栏拆分为 WebAPI + MQTT 双指示器

`MainWindowViewModel` 将单一连接状态拆分为两组属性：

| 属性 | 含义 | 数据来源 |
|------|------|---------|
| `WebApiConnectionStatus` / `WebApiConnectionColor` | HTTP 可达性 | `GET /` 健康检查 |
| `MqttConnectionStatus` / `MqttConnectionColor` | MQTT Broker 连接 | `GET api/system/state` → `Server.IsMqttConnected` |

`RefreshConnectionStatusAsync()` 统一刷新：先测 HTTP 可达性 → 若可达则调 `GetSystemState()` 取 MQTT 状态。UI 三色：
- 绿色 = 已连接
- 灰色 = 未连接
- 金色 = 未知（WebAPI 不可达时 MQTT 状态无意义）

#### 3. BaseUrl 纳入 ConfigViewModel 表单

- `ConfigViewModel` 新增 `BaseUrl` 可观察属性 + `NotifyBaseUrlChanged` 回调
- 配置表单首行新增"WebAPI 地址"输入框
- 保存时调用 `_configManager.SaveBaseUrl()` + 触发回调通知 MainWindow
- 切换到编辑模式时恢复已保存的 BaseUrl
- 摘要模式加载保存的 BaseUrl 显示

#### 4. 视图层清理

- `MainWindow.axaml`：顶部栏从"地址输入框 + 单一状态"改为"WebAPI 状态 + MQTT 状态"双指示器
- `ConfigView.axaml`：新增 BaseUrl 输入行；"退出"按钮改为"返回"（切换到摘要模式）
- `MainWindow.axaml.cs`：移除 `BaseUrlTextBox_LostFocus`（逻辑迁入 ViewModel）

#### 5. WebAPI.csproj 移除循环引用

删除 `<ProjectReference Include="..\AvaloniaApplication_ConfigLauncher\..."/>`，WebAPI 不依赖启动器。

---

## 三、Git 提交消息

```
feat(ConfigLauncher): 启动器新增 MQTT 连接状态显示，BaseUrl 纳入配置表单双向管理
```

**正文：**

1. SystemStateDto ServerStateDto 新增 IsMqttConnected 字段，供启动器 HTTP 查询 MQTT 状态
2. MainWindowViewModel 拆分为 WebApiConnectionStatus + MqttConnectionStatus 双指示器，统一 RefreshConnectionStatusAsync 刷新
3. ConfigViewModel 新增 BaseUrl 管理 + NotifyBaseUrlChanged 回调，保存时同步通知 MainWindow
4. ConfigView 新增 WebAPI 地址输入行，"退出"改为"返回"摘要模式
5. MainWindow 顶部栏改为 WebAPI/MQTT 双状态指示器，移除地址输入框（迁入配置页）
6. WebAPI.csproj 移除对 AvaloniaApplication_ConfigLauncher 的循环引用
7. 新增 7 个单元测试覆盖 BaseUrl 持久化、MQTT 状态渲染、连接状态刷新路径

---

## 四、本次提交详情

### 基本信息

| 字段 | 内容 |
|------|------|
| **提交时间** | 2026-06-03 |
| **作者** | NB11000 |
| **基于提交** | `f9b7eb7` — feat(MQTT): 通过 Will + Retained 机制实现设备在线状态监控，取代 $SYS 主题 (2026-06-03) |
| **变更统计（核心 11 文件）** | 11 files changed, +354 insertions, -53 deletions |

### 核心变更文件清单

| 状态 | 文件路径 | 变更说明 |
|------|----------|----------|
| 修改 | `SharedModels/SystemStateDto.cs` | ServerStateDto 新增 IsMqttConnected（+5 行） |
| 修改 | `WebAPI/Models/SystemStateDto.cs` | ServerStateDto 新增 IsMqttConnected（+5 行） |
| 修改 | `WebAPI/WebAPI.csproj` | 移除 ConfigLauncher 循环引用（-1 行） |
| 修改 | `ConfigLauncher.csproj` | 新增 IncludeNativeLibrariesForSelfExtract + PublishSingleFile（+6 行） |
| 修改 | `ViewModels/MainWindowViewModel.cs` | 双状态指示器 + RefreshConnectionStatusAsync + BaseUrl 回调（+74/-53 行） |
| 修改 | `ViewModels/ConfigViewModel.cs` | BaseUrl 管理 + SwitchToSummary + NotifyBaseUrlChanged（+23 行） |
| 修改 | `Views/ConfigView.axaml` | 新增 BaseUrl 输入行 + 按钮文案改为"返回"（+8 行） |
| 修改 | `Views/MainWindow.axaml` | 顶部栏改为 WebAPI/MQTT 双状态指示器（+35 行） |
| 修改 | `Views/MainWindow.axaml.cs` | 移除 BaseUrlTextBox_LostFocus（-6 行） |
| 修改 | `Test/...ConfigViewModelTests.cs` | 新增 BaseUrl 持久化/恢复/回调测试（+89 行） |
| 修改 | `Test/...MainWindowViewModelTests.cs` | 新增 MQTT 连接状态渲染测试 + 回调连线测试（+151 行） |

---

## 五、架构影响

| 维度 | 变更前 | 变更后 |
|------|--------|--------|
| 连接状态粒度 | 单一 WebAPI 可达性 | WebAPI + MQTT 双指标独立显示 |
| MQTT 状态获取方式 | 无（启动器不感知） | HTTP `GET api/system/state` → `Server.IsMqttConnected` |
| BaseUrl 管理 | MainWindow 顶部栏编辑 + LostFocus 保存 | ConfigView 表单内编辑 + ViewModel 回调传播 |
| SystemStateDto | ServerStateDto 仅含 IsApiAlive | 新增 IsMqttConnected 字段 |
| WebAPI 项目依赖 | 引用 ConfigLauncher（潜在循环） | 仅引用 SharedModels + ConsoleApp1 |

```
MainWindow.axaml 顶部栏（变更前）：
  [WebAPI 地址输入框] [● WebAPI状态]

MainWindow.axaml 顶部栏（变更后）：
  [● WebAPI: 已连接/未连接] [● MQTT: 已连接/未连接/—]

ConfigView.axaml（变更前）：
  Broker主机 / 端口 / MachineId / 用户名 / 密码 / TLS
  [保存并启动] [退出]

ConfigView.axaml（变更后）：
  WebAPI地址 / Broker主机 / 端口 / MachineId / 用户名 / 密码 / TLS
  [保存并启动] [返回]
```

---

## 六、审核报告

> 审查范围：`SystemStateDto.cs（两处）`、`MainWindowViewModel.cs`、`ConfigViewModel.cs`、View axaml、测试文件

### 通过项

| # | 检查点 | 详情 |
|---|--------|------|
| 1 | IsMqttConnected 字段一致性 | SharedModels 和 WebAPI 的 ServerStateDto 同步新增，字段名和类型一致 |
| 2 | 双状态刷新逻辑 | HTTP 可达时取 MQTT 状态，不可达时 MQTT 显示"—"（金色），三级分支无遗漏 |
| 3 | BaseUrl 回调链路 | ConfigVm → NotifyBaseUrlChanged → MainWindow.OnBaseUrlChanged → RefreshConnectionStatusAsync，链路完整 |
| 4 | 循环引用移除 | WebAPI 去掉 ConfigLauncher 引用，不影响编译（无类型依赖） |
| 5 | 测试覆盖 | 7 个新测试：HTTP 可达/MQTT 连接、HTTP 不可达、MQTT 断连、BaseUrl 持久化/恢复/回调 |

### 遗留建议（非阻塞）

| # | 严重度 | 位置 | 建议 |
|---|--------|------|------|
| 1 | 低 | `MainWindowViewModel.RefreshConnectionStatusAsync` | GetSystemState 异常时 MQTT 状态回退为"—"，建议增加重试计数器避免间歇性故障被静默 |
| D36 | 低 | `ConfigView.axaml` "返回"按钮 | 新增 SwitchToSummary 命令在 ModeA 可见，ModeB 下应隐藏或禁用 |

---

## 七、后续步骤预览（不在本次范围）

- 启动器连接状态自动刷新（定时轮询或事件驱动），替代手动 RefreshConnectionStatusAsync
- `events/state_changed` 通过 SignalR 推送到启动器，实现实时状态更新
