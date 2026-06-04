# Implementation Plan: 设备参数面板

## Modules

### 1. SharedModels — 配置 DTO 迁移
**单一职责**：将四个设备配置类从 WebAPI 项目移至 SharedModels，使启动器和 WebAPI 共享同一类型定义，消除硬编码重复。

### 2. WebAPI — 配置 HTTP 端点
**单一职责**：为 LidarAlgorithm 和 Persistence 两节新增 HTTP 读写与 default 端点，补齐 Radar 的 default 端点。遵循现有 Controller 风格。

### 3. LauncherHttpClient — 配置 API 调用
**单一职责**：在启动器的 HTTP 客户端中新增 4 组配置的 read / update / getDefault 方法，封装 JSON 序列化和错误处理。

### 4. DeviceConfigViewModel — 参数表单状态管理
**单一职责**：管理四节配置的表单状态，编排加载/保存/刷新/恢复默认的 HTTP 调用流程，控制子切段可见性，暴露表单验证和状态消息。

### 5. DeviceConfigView — 参数面板 UI
**单一职责**：渲染设备参数 Tab 的内容，包含顶部水平子 Tab 切换四节、每节表单布局、三按钮操作栏、状态栏。遵循现有 Avalonia 样式和卡片式布局。

### 6. MainWindow 集成
**单一职责**：在 TabControl 新增第 4 个 Tab "设备参数"，根据 WebAPI 连接状态联动其可见性和可用性。

---

## Interfaces

### SharedModels → ConfigLauncher / WebAPI
```csharp
// 四个配置 DTO（移至 SharedModels，namespace 统一为 SharedModels）
class CaptureCardConfig    { DeviceId, SyncChannelIndex, SampleRate, ClockSourceIndex, HalfFullThreshold, TriggerSourceIndex, RangeIndex }
class RadarConfig          { LaserPower, LaserModulationFrequency, SerialPort, BaudRate }
class LidarAlgorithmConfig { GainEqualizationCoefficient, KConstant, ReceiverApertureD_m, PathLengthL_m, Cn2WindowFrames, FernaldBoundaryDistance_m, LaserWavelength_nm, AngstromExponent, DarkCurrentSampleCount, SampleRateHz, BlindZoneDistance_m }
class PersistenceSettings  { DataDirectory }
```

### WebAPI → ConfigLauncher (HTTP endpoints)
```
POST api/collector/command/config/read      → CaptureCardConfig     [已有]
POST api/collector/command/config/update    → CaptureCardConfig     [已有]
GET  api/collector/command/config/default   → CaptureCardConfig     [已有]
POST api/laser/config/read                  → RadarConfig           [已有]
POST api/laser/config/update                → RadarConfig           [已有]
POST api/laser/config/default               → RadarConfig           [新增]
POST api/lidar/config/read                  → LidarAlgorithmConfig  [新增]
POST api/lidar/config/update                → LidarAlgorithmConfig  [新增]
POST api/lidar/config/default               → LidarAlgorithmConfig  [新增]
POST api/persistence/config/read            → PersistenceSettings   [新增]
POST api/persistence/config/update          → PersistenceSettings   [新增]
POST api/persistence/config/default         → PersistenceSettings   [新增]
```

### LauncherHttpClient → DeviceConfigViewModel
```csharp
Task<CaptureCardConfig?>    GetCaptureCardConfig()
Task<CaptureCardConfig?>    UpdateCaptureCardConfig(CaptureCardConfig)
Task<CaptureCardConfig?>    GetDefaultCaptureCardConfig()
Task<RadarConfig?>           GetRadarConfig()
Task<RadarConfig?>           UpdateRadarConfig(RadarConfig)
Task<RadarConfig?>           GetDefaultRadarConfig()
Task<LidarAlgorithmConfig?>  GetLidarConfig()
Task<LidarAlgorithmConfig?>  UpdateLidarConfig(LidarAlgorithmConfig)
Task<LidarAlgorithmConfig?>  GetDefaultLidarConfig()
Task<PersistenceSettings?>   GetPersistenceConfig()
Task<PersistenceSettings?>   UpdatePersistenceConfig(PersistenceSettings)
Task<PersistenceSettings?>   GetDefaultPersistenceConfig()
```

### DeviceConfigViewModel → DeviceConfigView (binding surface)
```csharp
// 子切段选择
int SelectedSubTabIndex    // 0=采集卡, 1=LiDAR, 2=激光器, 3=持久化
bool IsWebApiConnected     // 从 MainWindowVM 注入

// 采集卡表单属性（枚举字段为 int，UI 层用 ComboBox SelectedIndex 绑定）
int CaptureDeviceId, CaptureSyncChannelIndex, CaptureClockSourceIndex, ...
decimal CaptureSampleRate

// LiDAR 表单属性
double LidarGainEqCoeff, LidarKConst, LidarReceiverAperture, ...

// 雷达表单属性
int RadarLaserPower, RadarLaserModFreq
string RadarSerialPort
int RadarBaudRate

// 持久化表单属性
string PersistenceDataDirectory

// 操作命令
IAsyncRelayCommand SaveCommand      // 保存当前子节
IAsyncRelayCommand RefreshCommand   // 重新从 WebAPI 加载当前子节
IAsyncRelayCommand ResetDefaultCommand  // 加载默认值填充表单

// 状态
string StatusMessage   // "已保存" / "保存失败: ..." 等
bool IsBusy             // HTTP 调用进行中
```

### DeviceConfigViewModel → MainWindowViewModel
```csharp
// MainWindowViewModel 注入 LauncherHttpClient 实例和 WebAPI 连接状态回调
void SetWebApiConnected(bool connected) // 控制 IsWebApiConnected
```

---

## Data Flow

### Happy Path: 查看并保存采集卡参数
1. 用户切到"设备参数"Tab → View 加载，触发 `LoadCaptureCardConfig()`
2. VM 调用 `LauncherHttpClient.GetCaptureCardConfig()` → WebAPI `POST api/collector/command/config/read`
3. WebAPI 返回 JSON → 反序列化为 `CaptureCardConfig` → VM 填充各表单属性
4. 用户修改 `SampleRate` 从 1000 改为 2000
5. 用户点"保存" → VM 构建 `CaptureCardConfig` 对象 → `LauncherHttpClient.UpdateCaptureCardConfig()`
6. WebAPI 写入 `DeviceConfig.json`、通知子进程、刷新内存配置 → 返回更新后对象
7. VM 用返回值刷新表单 → 状态栏绿色"已保存"

### Error Path: WebAPI 不可达
1. 用户点"保存" → HTTP 调用超时或连接拒绝
2. `LauncherHttpClient` 返回 `null`（或抛出）
3. VM 状态栏红色"保存失败：无法连接到 WebAPI"

### Error Path: WebAPI 返回 500
1. HTML 响应或非预期 JSON → 反序列化失败
2. VM 状态栏红色"保存失败：服务器内部错误"

### Edge Case: WebAPI 未启动时切换 Tab
1. `IsWebApiConnected = false` → Tab 内容不可见/禁用
2. WebAPI 启动后 `IsWebApiConnected` 变为 `true` → Tab 内容显示，自动触发加载

### Edge Case: 用户在子切段间快速切换
1. 离开当前节时正在进行的 HTTP 调用被取消（通过 CTS）
2. 新节的加载不受影响

---

## Key Technical Decisions

1. **四个配置类移至 SharedModels**：消除两项目间的类型重复定义。移动 `<Compile Include="..\SharedModels\*.cs" Link="..." />` 改为直接 `ProjectReference`。WebAPI 通过 `using SharedModels;` 引用。

2. **单 ViewModel 对多子节**：一个 `DeviceConfigViewModel` 管理全部四节属性，通过 `SelectedSubTabIndex` 切换可见表单区域。比四个 VM 各自一个 View 更轻量，配置节之间无共享状态冲突。

3. **枚举字段用 ComboBox**：`CaptureCardConfig` 已有 `Channel` / `ClockSource` 等字符串显示属性，ComboBox Items 绑定中文文本数组，`SelectedIndex` 绑定 int 属性。

4. **HTTP 端点命名**：LidarAlgorithm 使用 `/api/lidar/` 前缀（非 `/api/lidar-algorithm/`），Persistence 使用 `/api/persistence/`。与现有 `/api/laser/`、`/api/collector/` 路由风格一致。

5. **配置在 WebAPI 启动时加载到内存**：`Program.cs` 已在启动时通过 `ConfigHelper` 加载所有配置。新增端点需要把 LidarAlgorithm 和 Persistence 也加入启动加载流程（如果尚未加入）。

6. **不引入 ADR**：本设计不满足 ADR 的三个条件——易反转（Tab 可随时移除）、非意外（自然扩展）、无明显权衡。

---

## Test Strategy

| 模块 | 测试类型 | 重点关注 | 不测试 |
|------|----------|----------|--------|
| DeviceConfigViewModel | 单元测试 | 加载/保存/刷新/恢复默认的 HTTP 编排；子切段切换状态重置；错误消息生成 | Avalonia 绑定行为 |
| LauncherHttpClient | 单元测试 | 请求路径正确性；JSON 反序列化；超时/连接错误的 null 返回 | 实际网络调用 |
| WebAPI 端点 | 集成测试 | endpoint 可达性；config/update 写文件正确；config/read 返回当前值；config/default 返回出厂默认值 | ConfigHelper 内部逻辑 |
| MainWindow 集成 | 手动验证 | Tab 联动 WebAPI 状态 | — |
| DeviceConfigView | 跳过 | — | — |

---

## Vertical Slice Design

### Slice 1: SharedModels 迁移 (foundation, no deps)
- 将 CaptureCardConfig / RadarConfig / LidarAlgorithmConfig / PersistenceSettings 移入 SharedModels
- 更新 WebAPI 和 ConfigLauncher 的引用/using
- 确认编译通过

### Slice 2: WebAPI 新增端点 (depends on Slice 1)
- 新增 `LidarController`（route: `api/lidar`），包含 config/read、config/update、config/default
- 新增 `PersistenceController`（route: `api/persistence`），包含 config/read、config/update、config/default
- 在 `LaserController` 中新增 config/default 端点
- 确保 `Program.cs` 启动时加载 LidarAlgorithm 和 Persistence 配置到全局静态属性
- 新增集成测试

### Slice 3: LauncherHttpClient 扩展 (depends on Slice 1)
- 新增 12 个 config API 方法
- 新增单元测试
- 在 `MainWindowViewModel` 构造时注入 `LauncherHttpClient` 供 DeviceConfigVM 使用

### Slice 4: DeviceConfigViewModel (depends on Slice 3)
- 实现完整 VM：四节表单属性、子切段切换、加载/保存/刷新/恢复默认逻辑
- 新增单元测试

### Slice 5: DeviceConfigView + MainWindow 集成 (depends on Slice 4)
- 编写 DeviceConfigView.axaml
- MainWindow 新增第 4 个 Tab
- MainWindowViewModel 注册 DeviceConfigVM，注入依赖
- 手动验证完整交互流程
