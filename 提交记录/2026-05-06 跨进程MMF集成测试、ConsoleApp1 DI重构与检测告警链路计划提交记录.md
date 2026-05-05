# 提交记录

> 生成时间：2026-05-06
> 仓库：数据采集与检测系统 V2.0
> 分支：`main`

---

## 一、Git 提交消息

```
feat(di-test): ConsoleApp1 DI重构消除静态依赖、跨进程MMF端到端集成测试、检测告警链路计划制定
```

**正文：**

完成 ConsoleApp1 的依赖注入重构，消除 `Program.coreBus` / `Program.deviceconfig` / `Program.logger` / `Program.uISharedBuffer` 四处静态字段依赖。`Program.cs` 引入 `ServiceCollection` → `BuildServiceProvider()` 启动流程，`AD_Controlcs` 新增 DI 构造函数（`CoreDataBus`、`UISharedBuffer`、`CaptureCardConfig`、`ILogger`），所有内部引用从 `Program.X` 改为 `_field`。新建 `MMFWriter` 最小控制台项目作为跨进程测试子进程，新增 `CrossProcessCoreDataBusTests`（4 个端到端测试：Create→Open→Write→TryRead 往返、多点写入取最新、WriteIndex 跨进程可见、Open 不崩溃）和 `AD_ControlcsDITests`（5 个 DI 注入验证测试）。WebAPI.Tests 测试总数从 32 增至 41，全部通过。`.scratch/` 更新 7 个 Issue（#05 #06 #07 #11 #12 标记 done，#18 #19 新建）。制定检测告警链路实现计划（Proto 扩展 → gRPC → DetectionPublisherService → MQTT），拆分为 2 个垂直切片 issue。`实施计划.md` 移入 `开发计划/` 目录。

---

## 二、本次提交详情

### 基本信息

| 字段 | 内容 |
|------|------|
| **提交时间** | 2026-05-06 |
| **作者** | NB11000 |
| **基于提交** | `fe91f68` — `feat(consumer-lifecycle): 事件驱动消费者生命周期管理——Coordinator统一启停、持久化CSV落盘、低频MQTT发布` (2026-05-05) |
| **变更统计（20 文件）** | 20 files changed，912 insertions(+)，44 deletions(-) |

### 核心变更文件清单

| 状态 | 文件路径 | 变更说明 |
|------|----------|----------|
| 修改 | `ConsoleApp1/Program.cs` | 引入 DI 容器：`ServiceCollection` → `BuildServiceProvider()`，注册 `ILogger` / `CaptureCardConfig` / `UISharedBuffer` / `CoreDataBus` / `AD_Controlcs`，通过 `GetRequiredService<AD_Controlcs>()` 解析实例；`services` 从 `public static` 改为局部变量，`coreBus` 从 `public static` 改为 `private static`（+13/-4 行） |
| 修改 | `ConsoleApp1/Service/AD_Controlcs.cs` | 新增 DI 构造函数（`CoreDataBus`、`UISharedBuffer`、`CaptureCardConfig`、`ILogger` 四个参数）+ 四个 `private readonly` 字段；`Device_Opened()` / `ADThread()` / `AD_Start()` / 异常处理中全部 `Program.deviceconfig` → `_deviceConfig`、`Program.coreBus` → `_coreBus`、`Program.logger` → `_logger`、`Program.uISharedBuffer` → `_uISharedBuffer`（+17/-7 行） |
| 新建 | `MMFWriter/MMFWriter.csproj` | 最小 .NET 8 控制台项目，链接共享 `SharedMemoryClient.cs` 和 `StructuredSample.cs`（+16 行） |
| 新建 | `MMFWriter/Program.cs` | 跨进程 MMF 写入器：`Open(mapName)` → 循环 `Write(ref sample)` → `Dispose()`，支持命令行参数指定写入条数和通道值（+29 行） |
| 新建 | `WebAPI.Tests/CrossProcessCoreDataBusTests.cs` | 跨进程 MMF 端到端测试 4 个：`Create_SubprocessWritesOne_ReadBackSameData`、`SubprocessWritesFive_TryReadReturnsLatest`、`WriteIndex_VisibleAcrossProcesses`、`SubprocessOpen_DoesNotCrash`；通过 `Process.Start` 启动 `MMFWriter.exe` 子进程（+105 行） |
| 新建 | `WebAPI.Tests/AD_ControlcsDITests.cs` | AD_Controlcs DI 注入验证测试 5 个：`ServiceProvider_ResolvesAD_Controlcs_WhenAllDependenciesRegistered`、`AD_Controlcs_UsesInjectedCoreDataBus_SameInstance`、`_UsesInjectedUISharedBuffer_SameInstance`、`_UsesInjectedCaptureCardConfig_SameInstance`、`_UsesInjectedILogger_SameInstance`；通过反射读取 `private readonly` 字段验证注入实例一致性（+118 行） |
| 修改 | `WebAPI.Tests/WebAPI.Tests.csproj` | 新增 `CopyMMFWriter` MSBuild Target：编译 `MMFWriter` 项目并复制输出到测试输出目录，确保 `Process.Start` 可找到可执行文件（+10 行） |
| 修改 | `WebAPI/Service/LowFrequencyPublisher.cs` | 新增类级别 XML 文档注释 `/// <summary>低频发布服务（采集绑定）</summary>`（+3 行） |
| 修改 | `WebAPI/Service/PersistenceService.cs` | 新增类级别 XML 文档注释 `/// <summary>持久化服务（采集绑定）</summary>` + `RunLoopAsync` 方法注释及关键步骤行内注释（+8 行） |
| 修改 | `数据采集与检测系统V2.0.sln` | 新增 `MMFWriter` 项目引用及其所有配置平台映射（Debug/Release × Any CPU/x64/x86）（+12 行） |

### 跟踪文档变更

| 状态 | 文件路径 | 变更说明 |
|------|----------|----------|
| 修改 | `.scratch/05-cross-process-mmf/issue.md` | State: `ready-for-human` → `done`；4 个验收标准全部打勾 |
| 修改 | `.scratch/06-persistence-csv/issue.md` | 9 个验收标准全部打勾 |
| 修改 | `.scratch/07-lowfreq-mqtt/issue.md` | 9 个验收标准全部打勾 |
| 修改 | `.scratch/11-di-refactor/issue.md` | State: `ready-for-human` → `done`；3 个验收标准全部打勾 |
| 修改 | `.scratch/12-acquisition-bound-interface/issue.md` | 2 个验收标准全部打勾 |
| 新建 | `.scratch/18-detection-proto-grpc/issue.md` | Issue #18：Proto 扩展 + 子进程结构化检测发送（State: todo） |
| 新建 | `.scratch/19-detection-publisher-mqtt/issue.md` | Issue #19：DetectionPublisherService MQTT 发布服务（State: todo，Blocked by: #18） |
| 重命名 | `实施计划.md` → `开发计划/实施计划.md` | 移入 `开发计划/` 目录，集中管理设计文档 |
| 新建 | `开发计划/检测告警链路实现计划.md` | DetectionPublisherService 完整实现计划（7 步骤、设计决策表、Payload JSON 格式、MQTT topic 规范、验证方案）（+201 行） |

---

## 三、背景（Background）

在 2026-05-05 的提交 `fe91f68` 中，WebAPI 侧已通过 `AcquisitionLifecycleCoordinator` + `IAcquisitionBoundService` 接口完成了三个消费者服务（WaveformPublishService、PersistenceService、LowFrequencyPublisher）的生命周期统一管理。但 ConsoleApp1 子进程仍存在两处技术债务：

1. **ConsoleApp1 无 DI 容器**：`Program.cs` 中 `CoreDataBus`、`CaptureCardConfig`、`UISharedBuffer` 等关键依赖通过 `public static` 字段暴露，`AD_Controlcs` 等消费方直接引用 `Program.coreBus`、`Program.deviceconfig` 等全局静态变量。Issue #11 早已记录了消除这一反模式的需求。

2. **跨进程 MMF 无自动化测试**：`CoreDataBus` 基于 MMF（Memory-Mapped File）实现跨进程共享，但所有测试均在同一进程内运行，未验证 `Process.Start` 子进程 → 父进程 `TryReadLatestSingle()` 的真实跨进程数据通路。Issue #05 记录了端到端集成测试需求。

同时，随着检测告警链路需求（Issue #08）的推进，需要制定 Proto 扩展 + gRPC + MQTT 发布的完整实现计划。

---

## 四、问题（Problem）

### 1. 全局静态依赖破坏可测试性和可维护性

ConsoleApp1 中四处 `public static` 字段直接暴露内部状态：

```csharp
public static IServiceCollection services = new ServiceCollection();  // 未真正使用
public static CoreDataBus coreBus { get; private set; }
public static CaptureCardConfig deviceconfig { get; private set; }
public static UISharedBuffer uISharedBuffer { get; private set; }
public static ILogger logger { get; set; }
```

`AD_Controlcs` 内部 7 处直接引用 `Program.X`：
- `Program.deviceconfig.DeviceId`（设备打开）
- `Program.deviceconfig.SampleRate`（采样率计算）
- `Program.deviceconfig.SyncChannelIndex` / `RangeIndex` / `ClockSourceIndex` / `TriggerSourceIndex`（AD 参数配置）
- `Program.coreBus.Write(ref detArr[i])`（数据总线写入）
- `Program.logger.LogError(errorMsg)`（错误日志）
- `Program.uISharedBuffer.WriteSampleBatch(...)`（UI 波形写入）

这导致：
- **无法单元测试**：`AD_Controlcs` 与全局状态强耦合，无法注入 mock 或 stub
- **隐藏依赖**：阅读 `AD_Controlcs` 代码无法知晓其依赖，必须查看 `Program.cs` 的静态字段
- **与 WebAPI 侧 DI 实践不一致**：WebAPI 已全面使用 `Microsoft.Extensions.DependencyInjection` 管理生命周期

### 2. 跨进程 MMF 路径从未端到端验证

所有现有 `CoreDataBus` 测试（`CoreDataBusTests`、`TimeHelperTests`、`DetectionChannelTests`）均在进程内运行。`Create()` → 子进程 `Open()` → 子进程 `Write()` → 父进程 `TryRead()` 的完整跨进程链路缺少自动化回归测试。MMF 的正确性依赖于：
- 内存映射文件在 `Create()` 和 `Open()` 之间的名称一致性
- `MemoryBarrier` 在跨进程间的正确配对
- `WriteIndex` 的 `Volatile.Read` 跨进程可见性

没有这些测试，任何对 `SharedMemoryClient` 的修改都可能静默破坏跨进程通信。

### 3. 检测告警链路缺少实现计划

ConsoleApp1 的 Detection 线程已通过 `SendErrorMessage` 将遮挡告警作为纯文本 Error 消息发送到 WebAPI，丢失了 `StructuredSample` 的全部上下文（Timestamp、CH1、CH2、Time）。需要制定从 Proto 定义到 MQTT 发布的完整方案。

---

## 五、解决方案（Solution）

### 整体思路

**ConsoleApp1 DI 化 + 跨进程测试子进程 + 计划文档化** —— 将 ConsoleApp1 的依赖管理从全局静态变量迁移到 DI 容器，新建独立的最小控制台项目 `MMFWriter` 作为跨进程测试子进程，编写端到端集成测试覆盖 MMF 跨进程全路径，制定检测告警链路 7 步实现计划并拆分为 2 个垂直切片 issue。

### 具体实施

#### 1. ConsoleApp1 DI 容器引入（Program.cs，+13/-4 行）

**变更前（全局静态）：**
```csharp
public static IServiceCollection services = new ServiceCollection();
public static CoreDataBus coreBus { get; private set; }
// 各服务直接引用 Program.coreBus、Program.deviceconfig 等
```

**变更后（DI 容器）：**
```csharp
// 构建 DI 容器
var services = new ServiceCollection();
services.AddSingleton<ILogger>(logger);
services.AddSingleton(deviceconfig);
services.AddSingleton(uISharedBuffer);
services.AddSingleton(coreBus);
services.AddSingleton<AD_Controlcs>();
_serviceProvider = services.BuildServiceProvider();
aD_Controlcs = _serviceProvider.GetRequiredService<AD_Controlcs>();
```

关键设计决策：
- `coreBus` 从 `public static` 降级为 `private static`，仅保留用于 `Dispose` 资源释放
- `deviceconfig` 保留 `public static`（Avalonia UI 主窗口绑定需要），但 `AD_Controlcs` 不再通过它获取配置
- `_serviceProvider` 为 `private static`，不对外暴露容器
- DI 初始化在 `ConfigHelper.ReadDeviceConfig()` 之后、`GrpcClient.Initialize()` 之前，保证时序不变

#### 2. AD_Controlcs DI 构造函数（+17/-7 行）

**新增 DI 构造函数：**
```csharp
private readonly CoreDataBus _coreBus;
private readonly UISharedBuffer _uISharedBuffer;
private readonly CaptureCardConfig _deviceConfig;
private readonly ILogger _logger;

public AD_Controlcs(CoreDataBus coreBus, UISharedBuffer uISharedBuffer,
    CaptureCardConfig deviceConfig, ILogger logger)
{
    _coreBus = coreBus;
    _uISharedBuffer = uISharedBuffer;
    _deviceConfig = deviceConfig;
    _logger = logger;
    cts = new CancellationTokenSource();
    CreateNewDataChannel();
}
```

保留无参构造函数（向后兼容，`CaptureCardConfig` 等地方可能有直接 new 的场景）。

**内部引用替换（7 处）：**

| 位置 | 变更前 | 变更后 |
|------|--------|--------|
| `Device_Opened()` | `Program.deviceconfig.DeviceId` | `_deviceConfig.DeviceId` |
| `Device_Opened_again()` | `Program.deviceconfig.DeviceId` | `_deviceConfig.DeviceId` |
| `ADThread()` 采样率计算 | `Program.deviceconfig.SampleRate` | `_deviceConfig.SampleRate` |
| `ADThread()` CoreDataBus 写入 | `Program.coreBus.Write(...)` | `_coreBus.Write(...)` |
| `ADThread()` UI 波形写入 | `Program.uISharedBuffer.WriteSampleBatch(...)` | `_uISharedBuffer.WriteSampleBatch(...)` |
| `AD_Start()` AD 参数配置 | `Program.deviceconfig.SyncChannelIndex` / `RangeIndex` / `ClockSourceIndex` / `TriggerSourceIndex` | `_deviceConfig.SyncChannelIndex` / `RangeIndex` / `ClockSourceIndex` / `TriggerSourceIndex` |
| 异常处理（2 处） | `Program.logger.LogError(...)` | `_logger.LogError(...)` |

#### 3. MMFWriter 最小测试子进程（2 文件，+45 行）

**MMFWriter.csproj：**
- .NET 8 控制台项目，`<AllowUnsafeBlocks>True</AllowUnsafeBlocks>`
- 通过 `<Compile Include="..\ConsoleApp1\..." Link="..." />` 链接共享 `SharedMemoryClient.cs` 和 `StructuredSample.cs`，避免代码复制

**MMFWriter/Program.cs：**
```csharp
var bus = new CoreDataBus(mapName);
bus.Open();
for (int i = 0; i < count; i++)
{
    var sample = new StructuredSample { Timestamp = i, ... };
    bus.Write(ref sample);
}
bus.Dispose();
```

命令行：`MMFWriter <mapName> <count> [ch1_0] [ch2_0] ...`，支持传入通道数据验证数据一致性。

#### 4. 跨进程 MMF 端到端测试（CrossProcessCoreDataBusTests，+105 行，4 tests）

| 测试方法 | 验证行为 |
|----------|----------|
| `Create_SubprocessWritesOne_ReadBackSameData` | WebAPI `Create` → 子进程 `Open` + 写入 1 条 (CH1=3.14, CH2=42.0) → `TryReadLatestSingle` 读到完全相同数据 |
| `SubprocessWritesFive_TryReadReturnsLatest` | 子进程写入 5 条 → `TryReadLatestSingle` 读到第 5 条 (Timestamp=4, CH1=5.0, CH2=50.0)，验证非首条 |
| `WriteIndex_VisibleAcrossProcesses` | 写入前 `WriteIndex=0` → 子进程写入 3 条 → `WriteIndex=3`，验证 `Volatile.Read` 跨进程可见性 |
| `SubprocessOpen_DoesNotCrash` | 子进程 `Open` + 写入 0 条 → 退出码 0，验证空操作不崩溃 |

关键实现细节：
- 每个测试使用 `Guid.NewGuid():N` 生成唯一 `mapName`，避免测试间冲突
- `IDisposable` 确保 `_bus?.Dispose()` 清理
- `Process.Start` + `WaitForExit(10_000)` 10 秒超时保护
- 子进程失败时通过 `StandardError` 输出完整错误信息

**MMFWriter 构建集成：**
```xml
<Target Name="CopyMMFWriter" AfterTargets="Build">
  <MSBuild Projects="..\MMFWriter\MMFWriter.csproj" Targets="Build" />
  <Copy SourceFiles="@(MMFWriterOutput)" DestinationFolder="$(OutputPath)" />
</Target>
```

WebAPI.Tests 构建时自动编译 MMFWriter 并复制到测试输出目录。

#### 5. AD_Controlcs DI 注入验证测试（AD_ControlcsDITests，+118 行，5 tests）

| 测试方法 | 验证行为 |
|----------|----------|
| `ServiceProvider_ResolvesAD_Controlcs_WhenAllDependenciesRegistered` | DI 容器注册全部依赖后 `GetRequiredService<AD_Controlcs>()` 成功解析，`LastStatusMessage` = "采集卡未打开" |
| `AD_Controlcs_UsesInjectedCoreDataBus_SameInstance` | 反射读取 `_coreBus` 字段，`Assert.Same` 验证与注册实例严格相同 |
| `_UsesInjectedUISharedBuffer_SameInstance` | 同上，验证 `_uISharedBuffer` 注入一致性 |
| `_UsesInjectedCaptureCardConfig_SameInstance` | 同上，验证 `_deviceConfig` 注入一致性 |
| `_UsesInjectedILogger_SameInstance` | 同上，验证 `_logger` 注入一致性 |

使用 `NullLogger.Instance` 作为测试替身，不产生日志输出。

#### 6. Issue 工作流更新（7 个 .scratch 文件）

| Issue | 状态变更 | 说明 |
|-------|----------|------|
| #05 cross-process-mmf | ready-for-human → **done** | 4 个验收标准全部打勾 |
| #06 persistence-csv | acceptance criteria → **全部打勾** | 9 个验收标准已实现（上一个提交完成） |
| #07 lowfreq-mqtt | acceptance criteria → **全部打勾** | 9 个验收标准已实现（上一个提交完成） |
| #11 di-refactor | ready-for-human → **done** | 3 个验收标准全部打勾 |
| #12 acquisition-bound-interface | acceptance criteria → **全部打勾** | 2 个验收标准已实现（上一个提交完成） |
| #18 detection-proto-grpc | **新建** | Proto 扩展 + 子进程结构化检测发送（State: todo） |
| #19 detection-publisher-mqtt | **新建** | DetectionPublisherService MQTT 发布服务（State: todo, Blocked by: #18） |

#### 7. 文档整理

- `实施计划.md` 移入 `开发计划/实施计划.md`，集中管理设计文档
- 新建 `开发计划/检测告警链路实现计划.md`（201 行）：
  - Context 背景分析
  - 7 项设计决策表（数据传递、MQTT topic、生命周期、告警分级、传输粒度、内部事件、Payload 格式）
  - 7 步实现计划（Proto → GrpcClient → AD_Controlcs → DetectionPublisherService → GrpcServiceImpl → Program.cs → 重新生成 gRPC）
  - Files Changed Summary 表格
  - Verification 4 步验证方案

#### 8. 解决方案结构更新

`sln` 新增 `MMFWriter` 项目及其 Debug/Release × Any CPU/x64/x86 全部 6 个平台配置映射。

---

## 六、架构影响

| 维度 | 变更前 | 变更后 |
|------|--------|--------|
| ConsoleApp1 依赖管理 | 全局静态字段（`Program.coreBus` 等） | `Microsoft.Extensions.DependencyInjection` 容器管理 |
| AD_Controlcs 可测试性 | 无法注入 mock/stub | 可通过 DI 构造函数注入测试替身 |
| 跨进程 MMF 测试 | 0 个端到端测试 | 4 个 Process.Start 跨进程测试 |
| AD_Controlcs DI 测试 | 0 个 | 5 个注入验证测试 |
| WebAPI.Tests 总数 | 32 | 41 |
| 检测告警链路 | 无计划 | 完整 7 步计划 + 2 个垂直切片 issue |
| 文档组织 | 实施计划散落在根目录 | 全部设计文档集中 `开发计划/` 目录 |

**不影响**：
- 生产数据通路：子进程 `_coreBus.Write(ref detArr[i])` 行为不变，仅获取 `CoreDataBus` 实例的方式从 static 变为 DI 注入
- gRPC 双向流通信链路
- `CaptureCardConfig` 仍保留 `public static`（Avalonia UI 绑定需要）
- `CoreDataBus` 仍保留 `private static` 引用用于 `Dispose()`
- MQTT RPC 路由和事件发布逻辑
- UISharedBuffer 高频波形链路

---

## 七、审核报告

> 审查范围：`ConsoleApp1/Program.cs`、`ConsoleApp1/Service/AD_Controlcs.cs`、`MMFWriter/`（2 文件）、`WebAPI.Tests/`（3 文件）、`WebAPI/Service/`（2 文件）、`.scratch/`（7 文件）、`开发计划/`（2 文件）、`sln`

### 通过项

| # | 检查点 | 详情 |
|---|--------|------|
| 1 | DI 注册完整性 | `Program.cs` 中 5 个服务注册（ILogger、CaptureCardConfig、UISharedBuffer、CoreDataBus、AD_Controlcs）覆盖 AD_Controlcs 构造函数全部 4 个参数 |
| 2 | DI 初始化时序 | DI Build 在 `ConfigHelper.ReadDeviceConfig()` 和 `SharedMemoryClient` 初始化之后、`GrpcClient.Initialize()` 和父进程监控之前，不改变启动时序 |
| 3 | 静态字段可见性降级 | `coreBus` 从 `public static` → `private static`；`services` 从 `public static` → 局部变量；`_serviceProvider` 为 `private static` |
| 4 | 向后兼容 | `AD_Controlcs` 保留无参构造函数；`deviceconfig` / `uISharedBuffer` / `logger` 保留 `public static`（Avalonia UI 绑定需要） |
| 5 | 跨进程测试隔离 | 每个测试独立 `mapName = Guid.NewGuid():N` + `IDisposable` 清理，测试间无干扰 |
| 6 | 子进程超时保护 | `WaitForExit(10_000)` 10 秒超时，避免 CI 挂死 |
| 7 | MMFWriter 构建集成 | `CopyMMFWriter` MSBuild Target 自动编译和复制，开发者无需手动构建 |
| 8 | DI 测试注入验证 | 5 个测试通过反射 `GetField("_fieldName", NonPublic | Instance)` 验证 4 个注入字段与注册实例 `Assert.Same` |
| 9 | Issue 状态一致性 | 5 个 done issue 的 acceptance criteria 全部打勾；2 个新 issue 的 Blocked by 关系正确 |
| 10 | 计划文档完整性 | 检测告警链路计划含 Context → Design Decisions → 7 Steps → Files Changed → Verification 全流程 |

### 已解决问题

| # | 严重度 | 位置 | 问题描述 | 解决 |
|---|--------|------|----------|------|
| 1 | **高** | ConsoleApp1 | `AD_Controlcs` 通过 `Program.coreBus` 等静态字段访问依赖，无法单元测试 | DI 构造函数注入，5 个测试验证注入一致性 |
| 2 | **高** | 测试 | 跨进程 MMF 通信无自动化测试覆盖 | MMFWriter 子项目 + 4 个端到端测试 |
| 3 | **中** | ConsoleApp1 | `public static IServiceCollection services` 声明但未使用，误导开发者 | 改为局部变量，在 `Main` 内完成 DI Build |
| 4 | **低** | 计划 | 检测告警链路无文档，需求散落在口头讨论中 | 201 行完整实现计划 + 2 个 issue |

### 遗留建议（非阻塞）

| # | 严重度 | 位置 | 建议 |
|---|--------|------|------|
| 1 | **提示** | `AD_Controlcs` | 无参构造函数保留用于兼容，未来清理所有 `new AD_Controlcs()` 调用后可移除 |
| 2 | **提示** | `Program.cs` | `deviceconfig` / `uISharedBuffer` / `logger` 仍为 `public static`（Avalonia UI 绑定需要），后续 Avalonia 引入 DI 后可一并消除 |
| 3 | **提示** | `CrossProcessCoreDataBusTests` | 当前仅验证 `StructuredSample` 的 3 个字段（Timestamp、CH1、CH2），后续可扩展验证全部 12 字段 |

---

## 八、后续步骤预览（不在本次范围）

- 步骤 18（Issue #18）：Proto 扩展 + 子进程结构化检测发送（`DetectionAlert` message + `SendDetectionAlert()` + `Detection()` 改用新方法）
- 步骤 19（Issue #19）：DetectionPublisherService MQTT 发布服务（`IAcquisitionBoundService` + `GrpcServiceImpl` 路由 + DI 注册）
- 步骤 15（Issue #08）：检测线程完整逻辑——遮挡/噪声/畸变三通道检测 + 告警分级
- 步骤 16（Issue #09）：Vis 反演接入 Analysis 线程
- 步骤 17（Issue #10）：Cn² 反演接入 Analysis 线程
