# 提交记录

> 生成时间：2026-05-05
> 仓库：数据采集与检测系统 V2.0
> 分支：`main`

---

## 一、Git 提交消息

```
feat(consumer-lifecycle): 事件驱动消费者生命周期管理——Coordinator统一启停、持久化CSV落盘、低频MQTT发布
```

**正文：**

引入 `AcquisitionLifecycleCoordinator` 集中管理所有数据流水线消费者的生命周期。定义 `IAcquisitionBoundService` 接口作为统一契约（`Start()` / `Stop()` / `RequiresMqttConnection`），协调器订阅 `SystemStateService` 的双状态事件（`AcquiringStateChanged` + 新增 `MqttConnectionStateChanged`），按 `CanRun = Acquiring && (!RequiresMqtt || MqttConnected)` 公式分发启停。新建 `PersistenceService`（30s 周期，CoreDataBus → CSV 按小时分片，`RequiresMqttConnection=false`）和 `LowFrequencyPublisher`（7s 周期，CoreDataBus → JSON → `daq/{id}/lowfreq`，`RequiresMqttConnection=true`）。改造 `WaveformPublishService` 移除 `BackgroundService` 继承和事件订阅，改为实现 `IAcquisitionBoundService`。`MqttRpcBackgroundService` 解耦 `WaveformPublishService` 直接依赖，改为通过 `SystemStateService.UpdateMqttConnectionState()` 间接驱动。`CoreDataBus` 暴露 `ReferenceTick` / `ReferenceUtcTicks` 等 6 个公开只读属性供消费者调用 `TimeHelper.ToUtcDateTime()` 还原 UTC 时间。WebAPI.Tests 新增 11 个测试用例（Coordinator 事件分发 6 个、SystemStateService MQTT 状态 2 个、服务幂等模式 3 个），与已有 21 个合计 32 个全部通过。更新 CONTEXT.md 新增「采集绑定服务」「采集生命周期协调器」两个领域术语。`.scratch/` 更新 7 个 Issue（#06 #07 #12–#16）至 done，新增 PRD 文档 #17。ADR 0001 记录事件驱动 vs 信号驱动的架构决策。

---

## 二、本次提交详情

### 基本信息

| 字段 | 内容 |
|------|------|
| **提交时间** | 2026-05-05 |
| **作者** | NB11000 |
| **基于提交** | `5dbff9e` — `001` (2026-05-05) |
| **变更统计（14 文件）** | 14 files changed，694 insertions(+)，111 deletions(-) |

### 核心变更文件清单

| 状态 | 文件路径 | 变更说明 |
|------|----------|----------|
| 新建 | `WebAPI/Service/IAcquisitionBoundService.cs` | 接口定义：`Start()` / `Stop()` / `RequiresMqttConnection`（+9 行） |
| 新建 | `WebAPI/Service/AcquisitionLifecycleCoordinator.cs` | 协调器：订阅双事件，遍历 `IEnumerable<IAcquisitionBoundService>`，按 `CanRun = Acquiring && (!RequiresMqtt \|\| MqttConnected)` 公式分发启停（+52 行） |
| 新建 | `WebAPI/Service/PersistenceService.cs` | 持久化服务：30s 周期 `TryReadLatestSingle()` → `TimeHelper.ToUtcDateTime()` → CSV 按小时分片，`RequiresMqttConnection=false`（+125 行） |
| 新建 | `WebAPI/Service/LowFrequencyPublisher.cs` | 低频发布：7s 周期 `TryReadLatestSingle()` → JSON → `daq/{MachineId}/lowfreq`，QoS 1，`RequiresMqttConnection=true`（+136 行） |
| 修改 | `WebAPI/Service/SystemStateService.cs` | 新增 `MqttConnectionStateChanged` 事件 + `_mqttConnected` volatile 字段 + `UpdateMqttConnectionState(bool)` 方法（值不变不触发）（+21 行） |
| 修改 | `WebAPI/Service/WaveformPublishService.cs` | 移除 `BackgroundService` 继承和 `AcquiringStateChanged` 事件订阅，改为实现 `IAcquisitionBoundService` + `IDisposable`，保留 `lock` + `_isRunning` 幂等逻辑（-111/+119 行，净 -39 行） |
| 修改 | `WebAPI/Service/MqttRpcBackgroundService.cs` | 构造函数注入 `SystemStateService` 替代 `WaveformPublishService`；`ConnectAsync` 末尾 → `UpdateMqttConnectionState(true)`；`OnDisconnectedAsync` → `UpdateMqttConnectionState(false)`；`StopAsync` 新增状态更新（+3/-13 行） |
| 修改 | `WebAPI/Service/SharedMemoryServer.cs` | `CoreDataBus` 新增 `WriteIndex` / `ChannelCount` / `BufferLength` / `SampleRate` / `ReferenceTick` / `ReferenceUtcTicks` 六个公开只读属性（+7 行） |
| 修改 | `WebAPI/Program.cs` | `WaveformPublishService` 注册改为纯 `AddSingleton` + `AddSingleton<IAcquisitionBoundService>()`，去掉 `AddHostedService`；新增 `PersistenceService` / `LowFrequencyPublisher` / `AcquisitionLifecycleCoordinator` 注册（+12/-3 行） |
| 新建 | `WebAPI.Tests/AcquisitionLifecycleCoordinatorTests.cs` | Coordinator 6 个测试：采集启停、MQTT 连断、幂等、RequiresMqtt 条件分发（+149 行） |
| 新建 | `WebAPI.Tests/SystemStateServiceTests.cs` | MQTT 状态 2 个测试：值变触发事件、同值不触发（+44 行） |
| 新建 | `WebAPI.Tests/AcquisitionBoundServiceTests.cs` | 服务幂等 3 个测试：重复 Start、重复 Stop、StartAfterStop（+103 行） |
| 修改 | `WebAPI.Tests/WebAPI.Tests.csproj` | 新增 `WebAPI` 项目引用，使测试可访问 Coordinator / SystemStateService（+1 行） |

### 跟踪文档变更

| 状态 | 文件路径 | 变更说明 |
|------|----------|----------|
| 修改 | `CONTEXT.md` | 新增「生命周期」术语章节（采集绑定服务、采集生命周期协调器）；Relationships 更新 Coordinator 启停链路与服务 MQTT 依赖；Key Decisions 新增 #11（事件驱动）与 #12（不继承 BackgroundService）；Example dialogue 新增（+18/-3 行） |
| 新建 | `docs/adr/0001-event-driven-consumer-lifecycle.md` | 架构决策：拒绝信号驱动、BackgroundService 空实现、分散订阅三方案，采纳 Coordinator 集中启停（+18 行） |
| 修改 | `.scratch/06-persistence-csv/issue.md` | Label ready-for-agent → done；更新为 Singleton + IAcquisitionBoundService 设计；新增 Program.cs 注册验收标准 |
| 修改 | `.scratch/07-lowfreq-mqtt/issue.md` | Label ready-for-agent → done；更新为 Singleton + IAcquisitionBoundService 设计；新增 MQTT 断连自动停/重连自动启验收标准 |
| 新建 | `.scratch/12-acquisition-bound-interface/issue.md` | IAcquisitionBoundService 接口定义（Label: done） |
| 新建 | `.scratch/13-lifecycle-coordinator/issue.md` | AcquisitionLifecycleCoordinator 实现（Label: done） |
| 新建 | `.scratch/14-mqtt-state-event/issue.md` | SystemStateService 新增 MqttConnectionStateChanged 事件（Label: done） |
| 新建 | `.scratch/15-waveform-refactor/issue.md` | WaveformPublishService 改造（Label: done） |
| 新建 | `.scratch/16-mqttrpc-refactor/issue.md` | MqttRpcBackgroundService 解耦（Label: done） |
| 新建 | `.scratch/17-event-driven-lifecycle-prd/issue.md` | 消费者生命周期事件驱动化 PRD（Label: done） |

---

## 三、背景（Background）

在 2026-04-29 的提交 `383f728` 中，`WaveformPublishService` 已通过 `SystemStateService.AcquiringStateChanged` 事件实现采集状态驱动的启停，但其启停逻辑仍存在两处不足：

1. **仅波形发布受益**：持久化和低频发布仍设计为自驱定时器 BackgroundService，不感知采集状态，即使采集停止也会空转轮询 `CoreDataBus.TryReadLatestSingle()`。
2. **MQTT 断连逻辑手动耦合**：`MqttRpcBackgroundService` 直接注入并操控 `WaveformPublishService` 的 Start/Stop，启停逻辑散布在三处（`ConnectAsync` 末尾、`OnDisconnectedAsync`、`StopAsync`）。

同时，随着 Issue 工作流的推进，`CONTEXT.md` 中早已声明「生产消费者生命周期绑定」（Key Decision #9），但持久化和低频发布两个消费者并未实际遵循这一约定。需要一个系统性的机制将此约定落地到所有消费者。

---

## 四、问题（Problem）

### 1. 消费者生命周期与生产者脱节

持久化和低频发布服务设计为独立的 `BackgroundService`，通过 `PeriodicTimer` 自驱轮询 `CoreDataBus`。它们的运行完全不感知采集状态：

```
采集停止 → 子进程 Analysis 线程退出 → CoreDataBus.WriteIndex 不再推进
                                            ↓
PersistenceService 每 30s 醒来 → TryReadLatestSingle() → sample == default → 跳过
LowFrequencyPublisher 每 7s 醒来 → 同上
                                            ↓
                                    无意义的定时器唤醒和日志噪音
```

反之，如果消费者尚未准备就绪而生产者已经开始写入，也没有机制通知消费者。

### 2. MQTT 连接状态未形成全局感知

`MqttRpcBackgroundService` 内部的 `_mqttClient.IsConnected` 状态对外不可见。只有 `WaveformPublishService` 通过直接注入被手动启停。`LowFrequencyPublisher` 同样依赖 MQTT 连接，但没有任何机制在 MQTT 断连时通知它停止发布。

### 3. BackgroundService + ExecuteAsync 空实现是反模式

`WaveformPublishService` 继承 `BackgroundService` 的唯一目的是获得 DI 生命周期管理和 `Dispose`。`ExecuteAsync` 永远返回 `Task.CompletedTask`。这种模式在三个消费者中会复制三次。

### 4. 架构决策未文档化

从「信号驱动」转向「事件驱动」的决策、Coordinator 集中模式的引入、纯 Singleton 取代 BackgroundService 的选择——这些架构决策仅存在于口头讨论中，缺少正式的 ADR 记录和替代方案的推演。

---

## 五、解决方案（Solution）

### 整体思路

**事件驱动 + Coordinator 集中启停 + 接口统一契约** —— 将采集状态事件和 MQTT 连接状态事件收敛到 `SystemStateService` 作为唯一状态源，`AcquisitionLifecycleCoordinator` 集中订阅并分发启停信号，所有消费者实现统一接口 `IAcquisitionBoundService`。

### 具体实施

#### 1. 接口定义：IAcquisitionBoundService（+9 行）

```csharp
public interface IAcquisitionBoundService
{
    bool RequiresMqttConnection { get; }
    void Start();
    void Stop();
}
```

- `RequiresMqttConnection`：Coordinator 据此判断 `CanRun` 公式
- `Start()` / `Stop()`：线程安全幂等，实现方自行保证

#### 2. Coordinator：AcquisitionLifecycleCoordinator（+52 行）

核心逻辑：

```csharp
private void Apply()
{
    foreach (var service in _services)
    {
        bool canRun = _acquiring && (!service.RequiresMqttConnection || _mqttConnected);
        if (canRun)
            service.Start();
        else
            service.Stop();
    }
}
```

- 通过 `IEnumerable<IAcquisitionBoundService>` 发现所有服务，新增服务无需修改 Coordinator
- 自身不加锁，服务各自保证 `Start()`/`Stop()` 幂等
- 纯 Singleton，不继承 `BackgroundService`

事件流向：

```
SystemStateService.AcquiringStateChanged ──┐
                                            ├── Coordinator.Apply() ──┬── PersistenceService.Start/Stop
SystemStateService.MqttConnectionStateChanged┘                        ├── WaveformPublishService.Start/Stop
                                                                      └── LowFrequencyPublisher.Start/Stop
```

#### 3. SystemStateService 增强（+21 行）

```csharp
public event Action<bool>? MqttConnectionStateChanged;
private volatile bool _mqttConnected;

public void UpdateMqttConnectionState(bool isConnected)
{
    if (_mqttConnected == isConnected) return;
    _mqttConnected = isConnected;
    MqttConnectionStateChanged?.Invoke(isConnected);
}
```

- 与 `AcquiringStateChanged` 完全对称的事件模式
- `volatile bool` 缓存当前状态，仅在值变化时触发
- `MqttRpcBackgroundService` 在断连/重连/停止时调用此方法

#### 4. WaveformPublishService 改造（-39 行净减少）

| 变更项 | 变更前 | 变更后 |
|--------|--------|--------|
| 基类 | `BackgroundService` | 无（纯 `IDisposable`） |
| 接口 | 无 | `IAcquisitionBoundService` |
| 事件订阅 | 构造函数内 `AcquiringStateChanged += OnAcquiringStateChanged` | 移除，改由 Coordinator 调用 `Start()`/`Stop()` |
| ExecuteAsync | 空实现 `Task.CompletedTask` | 移除 |
| Program.cs 注册 | `AddSingleton` + `AddHostedService` | 仅 `AddSingleton` + `AddSingleton<IAcquisitionBoundService>()` |

保留：`lock` + `_isRunning` 双重检查的幂等逻辑、`PeriodicTimer` 波形发布循环、`Buffer.BlockCopy` 零分配转换。

#### 5. MqttRpcBackgroundService 解耦（+3/-13 行）

| 位置 | 变更前 | 变更后 |
|------|--------|--------|
| 构造函数 | 注入 `WaveformPublishService` | 注入 `SystemStateService` |
| `ConnectAsync` 末尾 | `_waveformPublishService.Start()` | `_systemStateService.UpdateMqttConnectionState(true)` |
| `OnDisconnectedAsync` | `_waveformPublishService.Stop()` | `_systemStateService.UpdateMqttConnectionState(false)` |
| `StopAsync` | 无 MQTT 状态更新 | `_systemStateService.UpdateMqttConnectionState(false)` |

不再直接操控任何消费者，仅更新自身状态到 SystemStateService。

#### 6. PersistenceService 新建（+125 行）

- `RequiresMqttConnection = false`：不因 MQTT Broker 波动影响本地落盘
- 30s 周期，`PeriodicTimer` → `CoreDataBus.TryReadLatestSingle()` → `TimeHelper.ToUtcDateTime()` → CSV
- 按小时分片：`{date}_{HH}.csv`，自动创建 `data/` 目录
- `FileStream` + `StreamWriter` + `FlushAsync`：保证写入可靠、异常不崩溃
- CSV Header：`Timestamp,UTC,CH1,CH2,Vis,Cn2,Temp,Humi,Press,WindSpd,Rain,WindDir`

#### 7. LowFrequencyPublisher 新建（+136 行）

- `RequiresMqttConnection = true`：MQTT 断连时 Coordinator 自动停，重连且采集中自动启
- 7s 周期，`PeriodicTimer` → `CoreDataBus.TryReadLatestSingle()` → JSON → `daq/{MachineId}/lowfreq`
- QoS 1（至少一次），复用 `MqttEventPublisher.MqttClient`，不创建新连接
- 无数据时跳过本周期，不发布
- JSON 含全部 12 字段 + `TimeHelper.ToUtcDateTime()` 还原后的 UTC 时间字符串

#### 8. CoreDataBus 公共属性（+7 行）

WebAPI 侧的 `SharedMemoryServer.CoreDataBus` 新增 6 个公共只读属性，与 ConsoleApp1 侧已有属性一致：

```csharp
public long WriteIndex => Volatile.Read(ref header->WriteIndex);
public int ChannelCount => header->ChannelCount;
public int BufferLength => header->BufferLength;
public int SampleRate => header->SampleRate;
public long ReferenceTick => header->ReferenceTick;
public long ReferenceUtcTicks => header->ReferenceUtcTicks;
```

`WriteIndex` 使用 `Volatile.Read` 保证与生产者 `MemoryBarrier` 配对。

#### 9. 测试覆盖（3 文件，+296 行，11 tests）

| 测试文件 | 测试数 | 覆盖行为 |
|----------|:------:|----------|
| `AcquisitionLifecycleCoordinatorTests` | 6 | 采集启停 → non-MQTT 服务启动；MQTT 未连接 → MQTT 服务不启；MQTT 连接后 → MQTT 服务启动；采集停止 → 全部停；MQTT 断连 → 仅 MQTT 服务停；同值不重复调 |
| `SystemStateServiceTests` | 2 | `UpdateMqttConnectionState` 值变触发事件；同值不触发 |
| `AcquisitionBoundServiceTests` | 3 | 重复 Start 幂等；重复 Stop 幂等；StartAfterStop 重新启动 |

使用 `SpyService`（记录 Start/Stop 调用次数）作为测试替身，不依赖真实 MMF 或 MQTT 连接。`TestService` 复刻 `lock` + `_isRunning` 模式验证幂等正确性。

#### 10. DI 注册改造（Program.cs，+12/-3 行）

```csharp
// WaveformPublishService — 改为纯 Singleton + 接口注册
builder.Services.AddSingleton<WaveformPublishService>();
builder.Services.AddSingleton<IAcquisitionBoundService>(sp => sp.GetRequiredService<WaveformPublishService>());

// PersistenceService — 纯 Singleton + 接口注册
builder.Services.AddSingleton<PersistenceService>();
builder.Services.AddSingleton<IAcquisitionBoundService>(sp => sp.GetRequiredService<PersistenceService>());

// LowFrequencyPublisher — 纯 Singleton + 接口注册
builder.Services.AddSingleton<LowFrequencyPublisher>();
builder.Services.AddSingleton<IAcquisitionBoundService>(sp => sp.GetRequiredService<LowFrequencyPublisher>());

// Coordinator — 通过 IEnumerable<IAcquisitionBoundService> 发现所有服务
builder.Services.AddSingleton<AcquisitionLifecycleCoordinator>();
```

移除了 `AddHostedService<WaveformPublishService>()`。`MqttRpcBackgroundService` 保留为 `AddHostedService`（它有真正的后台循环，需要 `ExecuteAsync`）。

---

## 六、架构影响

| 维度 | 变更前 | 变更后 |
|------|--------|--------|
| 消费者生命周期 | 波形发布事件驱动，持久化/低频自驱定时器 | 全部三个消费者统一由 Coordinator 事件驱动 |
| 服务类型 | Waveform 继承 BackgroundService，ExecuteAsync 空实现 | 全部纯 Singleton + IDisposable |
| 启停决策 | 波形：MqttRpcBackgroundService 手动管控；持久化/低频：无管控 | Coordinator 集中判断 CanRun 公式 |
| MQTT 状态感知 | 仅 MqttRpcBackgroundService 内部可见 | SystemStateService 公开，全局可订阅 |
| CoreDataBus API | 无公开时间校准属性 | 6 个只读属性，消费者可调用 TimeHelper |
| 测试 | 0 个 | 11 个新测试，全通过 |
| MqttRpcBackgroundService 依赖 | 注入 WaveformPublishService | 注入 SystemStateService，不再知道任何消费者的存在 |

**不影响**：
- 生产数据通路：子进程 Analysis → CoreDataBus / DetectionChannel 两路分流不变
- gRPC 双向流通信链路
- MQTT RPC 路由和事件发布逻辑
- UISharedBuffer 高频波形链路
- CoreBusHeader 内存布局（仅新增公共 getter，不修改结构体）

---

## 七、审核报告

> 审查范围：`WebAPI/Service/`（7 文件）、`WebAPI/Program.cs`、`WebAPI.Tests/`（4 文件）、`CONTEXT.md`、`docs/adr/0001`

### 通过项

| # | 检查点 | 详情 |
|---|--------|------|
| 1 | Coordinator 启停逻辑正确性 | 6 个单元测试覆盖全部状态组合：`Acquiring=F ∧ Mqtt=F` / `Acquiring=T ∧ Mqtt=F` / `Acquiring=T ∧ Mqtt=T` / 状态转换 |
| 2 | SystemStateService 事件幂等 | `UpdateMqttConnectionState` 在值不变时不触发事件，与 `UpdateCollectorState` 中 `if (oldAcquiring != newState.Acquiring)` 一致 |
| 3 | 服务幂等保护 | `lock` + `_isRunning` 双重检查，`Stop()` 中 `cts.Cancel()` 用 try-catch 包裹 ObjectDisposedException |
| 4 | 故障隔离 | `Program.cs` 中 `BackgroundServiceExceptionBehavior = Ignore`；每个消费者独立 `try-catch` 包裹循环体；一个消费者异常不影响其他 |
| 5 | 扩展性 | Coordinator 通过 `IEnumerable<IAcquisitionBoundService>` 发现服务，新消费者只需实现接口并注册，无需改 Coordinator 代码 |
| 6 | DI 注册一致性 | 三个服务均采用 `AddSingleton<TService>()` + `AddSingleton<IAcquisitionBoundService>(sp => sp.GetRequiredService<TService>())` 模式 |
| 7 | 线程安全 | 各服务 `Start()`/`Stop()` 使用 `lock` 保护 `_isRunning` 和 `_cts` 状态；Coordinator 不加锁，依赖服务幂等保证 |
| 8 | 时间校准 | `PersistenceService` 和 `LowFrequencyPublisher` 均通过 `CoreDataBus.ReferenceTick`/`ReferenceUtcTicks` + `Stopwatch.Frequency` + `TimeHelper.ToUtcDateTime()` 还原 UTC，与 CONTEXT.md Key Decision #4 一致 |
| 9 | 资源泄漏 | 所有服务实现 `IDisposable`，`_cts?.Dispose()` 清理 CTS |
| 10 | 向后兼容 | `WaveformPublishService` 的公开接口（Start/Stop/Dispose）行为不变，仅调用方从事件回调改为 Coordinator |

### 已解决问题

| # | 严重度 | 位置 | 问题描述 | 解决 |
|---|--------|------|----------|------|
| 1 | **高** | 持久化/低频发布 | 消费者生命周期与生产者脱节，采集停止后持续空转轮询 | Coordinator 事件驱动启停，采集停止自动退出 |
| 2 | **高** | LowFrequencyPublisher | 低频发布无 MQTT 断连感知，断连后仍尝试发布 | `RequiresMqttConnection=true`，Coordinator 在断连时自动调 Stop |
| 3 | **中** | WaveformPublishService | BackgroundService + ExecuteAsync 空实现是反模式 | 改为纯 Singleton + IDisposable |
| 4 | **中** | MqttRpcBackgroundService | 直接注入 WaveformPublishService，启停逻辑耦合 | 改为注入 SystemStateService，通过 MQTT 状态间接驱动 |
| 5 | **中** | CoreDataBus (WebAPI) | 缺少公开只读属性，消费者无法获取时间校准基准 | 新增 6 个公开属性 |
| 6 | **低** | 架构决策 | 信号驱动 vs 事件驱动决策未文档化 | ADR 0001 正式记录 |

### 遗留建议（非阻塞）

| # | 严重度 | 位置 | 建议 |
|---|--------|------|------|
| 1 | **提示** | `PersistenceService` | 持久化周期（30s）当前为编译时常量，建议后续通过 `IOptions` 或 `appsettings.json` 配置 |
| 2 | **提示** | `LowFrequencyPublisher` | 低频发布周期（7s）当前为编译时常量，建议后续支持配置化 |
| 3 | **提示** | `PersistenceService` | CSV 文件路径硬编码为 `data/` 子目录，未来可考虑通过配置指定绝对路径 |

---

## 八、后续步骤预览（不在本次范围）

- 步骤 13（Issue #11）：ConsoleApp1 DI 重构，消除 `Program.coreBus` static 依赖
- 步骤 14（Issue #05）：跨进程 MMF 端到端集成测试（`Process.Start` 子进程）
- 步骤 15（Issue #08）：检测线程完整逻辑——遮挡/噪声/畸变三通道检测
- 步骤 16（Issue #09）：Vis 反演接入 Analysis 线程
- 步骤 17（Issue #10）：Cn² 反演接入 Analysis 线程
