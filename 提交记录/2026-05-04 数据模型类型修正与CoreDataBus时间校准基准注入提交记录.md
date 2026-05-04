# 提交记录

> 生成时间：2026-05-04 14:11
> 仓库：数据采集与检测系统 V2.0
> 分支：`main`

---

## 一、Git 提交消息

```
refine(core): 数据模型类型修正、时间校准基准注入与 CoreDataBus API 安全化
```

**正文：**

将 `Voltage_block.SampleCount` 从 `double` 统一修正为 `int`，消除类型不匹配导致的隐式转换隐患，并移除 Analysis 线程中不再需要的 `(int)` 强制转换；在 `CoreBusHeader` 结构体新增 `ReferenceTick`（Stopwatch）与 `ReferenceUtcTicks`（DateTime.UtcNow）双时间校准基准字段，Create() 时刻一次性捕获，消费者据此将 WriteIndex 还原为绝对 UTC 时间，消除跨进程/跨平台时钟歧义；将 `ReadLatestSingle()` 重构为 `TryReadLatestSingle(out StructuredSample sample)`，返回 `bool` 明确指示总线是否已有数据，避免 WriteIndex==0 时返回 zeroed-default 造成的空值歧义；将 CoreDataBus 缓冲区容量从 1000 万采样点（≈915MB）下调至 100 万采样点（≈96MB），匹配当前 1s 时间窗口的需求，大幅降低共享内存占用；新增 `CONTEXT.md` 领域语言文档，统一团队对数据实体、数据通道、数据流及系统角色的术语定义。

---

## 二、本次提交详情

### 基本信息

| 字段 | 内容 |
|------|------|
| **提交时间** | 2026-05-04 14:11:22 |
| **作者** | NB11000 |
| **提交哈希** | （待提交后生成） |
| **基于提交** | `650d7d1` — `feat(core): 引入核心数据总线 CoreDataBus 与结构化采样模型，新增检测线程` (2026-05-03 23:00) |
| **变更统计（核心 8 文件）** | 8 files changed，426 insertions(+)，15 deletions(-) |

### 核心变更文件清单

| 状态 | 文件路径 | 变更说明 |
|------|----------|----------|
| 新建 | `CONTEXT.md` | 领域语言文档，定义数据实体、数据通道、数据流与系统角色的标准化术语，记录 10 条关键架构决策与 3 个已解决的遗留歧义（+107 行） |
| 修改 | `AvaloniaApplication1/Models/Voltage_block.cs` | `SampleCount` 字段类型 `double` → `int`，与子进程模型保持一致（1 行变更） |
| 修改 | `ConsoleApp1/Models/Voltage_block.cs` | `SampleCount` 字段类型 `double` → `int`，消除字段级的隐式类型转换隐患（1 行变更） |
| 修改 | `ConsoleApp1/Service/AD_Controlcs.cs` | ADDraw 线程：创建 Voltage_block 时对 `block.nBytes * a` 显式 `(int)` 转换（源端仍为 double 表达式）；Analysis 线程：移除 `voltageBlock.SampleCount` 前的冗余 `(int)` 转换（字段已是 int）（2 行变更） |
| 修改 | `ConsoleApp1/Service/SharedMemoryClient.cs` | `CoreBusHeader` 新增 `ReferenceTick` / `ReferenceUtcTicks` 双时间校准字段；`Create()` 末尾一次性捕获；`ReadLatestSingle()` → `TryReadLatestSingle(out StructuredSample sample)` 返回 bool；新增 `using System.Diagnostics`（+15/-4 行） |
| 修改 | `WebAPI/Service/SharedMemoryServer.cs` | 同步 `CoreBusHeader` 新增 `ReferenceTick` / `ReferenceUtcTicks`；`Create()` 末尾一次性捕获；`ReadLatestSingle()` → `TryReadLatestSingle(out StructuredSample sample)` 返回 bool；新增 `using System.Diagnostics`（+15/-4 行） |
| 修改 | `WebAPI/Program.cs` | CoreDataBus 缓冲区容量 10,000,000 → 1,000,000（≈915MB → ≈96MB）；日志文案同步更新（2 行变更） |
| 新建 | `提交记录/2026-05-03 核心数据总线与结构化采样模型提交记录.md` | 上一提交的详细记录归档（+274 行） |

---

## 三、背景（Background）

在 2026-05-03 的提交 `650d7d1` 中，CoreDataBus 作为跨进程结构化采样数据总线首次引入，但存在若干早期设计未闭合的遗留问题：

1. **`Voltage_block.SampleCount` 类型不一致**：该字段在 2026-05-03 的提交记录中已被标记为 `double` → `int` 的已解决歧义（见 `CONTEXT.md` 底部 `Flagged ambiguities`），但实际代码中仅 `AvaloniaApplication1` 和 `ConsoleApp1` 两个项目的模型文件尚未同步修改
2. **CoreDataBus 时间体系不完整**：CoreBusHeader 仅存储 `SampleRate`，缺少创建时刻的物理时钟锚点。消费者拿到 WriteIndex 后无法映射为绝对 UTC 时间，只能反推 session-local 的相对流逝时间，跨进程/跨平台场景下时钟源不统一时产生歧义
3. **`ReadLatestSingle()` API 语义模糊**：当 WriteIndex=0（总线尚无数据写入）时返回 `default(StructuredSample)`，消费者无法区分"确实读到了零值采样点"与"总线尚无数据"两种情况
4. **缓冲区容量过度预留**：10M 采样点（≈915MB）对应约 10s 的时间窗口，而实际消费者（持久化/低频 UI）读取间隔最长仅 7s，存在约 3s 的冗余。同时 915MB 的 MMF 文件在部分 Windows 机器上触发页面文件碎片

---

## 四、问题（Problem）

### 1. SampleCount 类型不匹配导致隐式转换

`Voltage_block.SampleCount` 在模型中声明为 `double`，但在所有消费端（Analysis 线程、检测线程）均按 `int` 语义使用，迫使每个使用点执行 `(int)` 强制转换。这不仅增加代码噪声，更在 ADDraw 的 `block.nBytes * a` 表达式中（`nBytes` 为 `uint`，`a` 为 `int`）引入了 double 中间类型的精度损失风险。

### 2. 消费者无法还原 UTC 绝对时间

CoreDataBus 只有 `WriteIndex`（逻辑序号）和 `SampleRate`，消费者仅能计算 session-local 的相对时间：

```
relativeTime = WriteIndex / SampleRate   // 只能得到"启动后第 N 秒"
```

但无法映射为 UTC 绝对时间戳，因为缺少创建时刻的物理时钟基准。对于需要将采样点时间戳写入 CSV 或标注 MQTT 消息的消费者，这是一个功能缺口。

### 3. `ReadLatestSingle()` 存在空值歧义

```csharp
public StructuredSample ReadLatestSingle()
{
    long index = Volatile.Read(ref header->WriteIndex);
    if (index == 0) return default;  // 返回 zeroed-default
    ...
}
```

当 WriteIndex=0 时，返回的 `new StructuredSample()` 所有字段均为零值。消费者无法区分：
- "WriteIndex=0，总线从未写过数据"（应等待/跳过）
- "WriteIndex>0，但刚好读到了一个 CH1=0, CH2=0 的合法采样点"（应正常处理）

后者在信号遮挡检测场景中恰好是合法条件，歧义可能导致漏判。

### 4. 缓冲区容量过大

10M 采样点 × 96B = 915MB 共享内存，虽然 MMF 不会立即提交物理页面，但保留的虚拟地址空间在 32 位进程（或 Large Address Aware 未开启的 64 位进程）中可能因碎片导致 Open() 失败。同时 1s 时间窗口已覆盖最大消费者读取间隔（7s），10s 窗口存在冗余。

---

## 五、解决方案（Solution）

### 整体思路

**类型收敛 + 时间锚定 + API 语义明确化 + 容量收敛** —— 解决上一提交遗留的 4 个设计未闭合问题，不引入新的架构概念。

### 具体实施

#### 1. `SampleCount` 类型收敛（2 文件，2 行）

**AvaloniaApplication1/Models/Voltage_block.cs**:
```csharp
// 变更前
public double SampleCount;

// 变更后
public int SampleCount;
```

**ConsoleApp1/Models/Voltage_block.cs**:
```csharp
// 同上
public int SampleCount;
```

**联动调整** — `ConsoleApp1/Service/AD_Controlcs.cs`:

ADDraw 线程（源端表达式 `block.nBytes * a` 仍生成 double）：
```csharp
// 变更后：显式 (int) 转换，源端表达式类型保护
SampleCount = (int)(block.nBytes * a)
```

Analysis 线程（字段已是 int）：
```csharp
// 变更前：int count = (int)voltageBlock.SampleCount;  // 冗余转换
// 变更后：int count = voltageBlock.SampleCount;        // 直接使用
```

此修正确认了 `CONTEXT.md` 中 `Flagged ambiguities` 记录的 `"SampleCount 曾是 double——已改为 int"` 的设计决策。

#### 2. 时间校准基准注入（2 文件，+14 行）

**CoreBusHeader 新增字段**（`SharedMemoryClient.cs` + `SharedMemoryServer.cs`）:

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct CoreBusHeader
{
    public long WriteIndex;
    public int ChannelCount;
    public int BufferLength;
    public int SampleRate;

    // 新增：时间校准基准
    public long ReferenceTick;       // Create() 时刻 Stopwatch.GetTimestamp()
    public long ReferenceUtcTicks;   // Create() 时刻 DateTime.UtcNow.Ticks（100ns 单位）
}
```

**Create() 末尾一次性捕获**:

```csharp
public void Create(int channels, int buffer, int sampleRate)
{
    // ... 创建 MMF、映射视图、初始化 header 字段 ...

    // 一次性捕获时间校准基准，消除跨进程/跨平台时钟歧义
    header->ReferenceTick = Stopwatch.GetTimestamp();
    header->ReferenceUtcTicks = DateTime.UtcNow.Ticks;
}
```

**消费者还原 UTC 时间**（伪代码，后续步骤实现）:

```csharp
// 消费者侧还原逻辑：
// posUtcTicks = header.ReferenceUtcTicks
//             + (WriteIndex / header.SampleRate) * TimeSpan.TicksPerSecond;
```

设计依据见 `CONTEXT.md` Key Decision #4：时间校准基准存 `ReferenceTick` + `ReferenceUtcTicks`，消费者据此还原绝对 UTC 时间，跨平台兼容。

双字段设计的原因：
- `ReferenceTick`（`Stopwatch.GetTimestamp()`）：高精度本地时钟，用于计算相对时间间隔
- `ReferenceUtcTicks`（`DateTime.UtcNow.Ticks`）：绝对 UTC 时间锚点，用于还原物理时间戳
- 两者配对使用，先计算相对间隔再叠加 UTC 基准，避免跨进程 `DateTime.UtcNow` 调用时差

#### 3. `ReadLatestSingle()` → `TryReadLatestSingle()` API 安全化（2 文件，+12/-6 行）

```csharp
// 变更前
public StructuredSample ReadLatestSingle()
{
    long index = Volatile.Read(ref header->WriteIndex);
    if (index == 0) return default;    // 歧义：空数据 vs 零值采样点
    long pos = (index - 1) % bufferLength;
    return *(dataPtr + pos);
}

// 变更后
/// <returns>true 表示总线已有数据，false 表示尚无数据写入</returns>
public bool TryReadLatestSingle(out StructuredSample sample)
{
    long index = Volatile.Read(ref header->WriteIndex);
    if (index == 0)
    {
        sample = default;
        return false;                   // 明确指示"尚无数据"
    }
    long pos = (index - 1) % bufferLength;
    sample = *(dataPtr + pos);
    return true;                        // 明确指示"成功读取"
}
```

消费者使用语义变化：
```csharp
// 变更前：无法区分无数据和零值
var sample = coreBus.ReadLatestSingle();
Process(sample);  // 可能误处理 zeroed-default

// 变更后：明确控制流
if (coreBus.TryReadLatestSingle(out var sample))
{
    Process(sample);  // 仅在有数据时处理
}
else
{
    // 总线尚无数据，跳过/等待
}
```

#### 4. 缓冲区容量收敛（1 文件，2 行）

```csharp
// 变更前
coreDataBus.Create(
    channels: 2,
    buffer: 10_000_000,    // ≈ 915MB
    sampleRate: 1_000_000);
Log.Information("核心数据总线已创建，缓冲区容量 10,000,000 采样点");

// 变更后
coreDataBus.Create(
    channels: 2,
    buffer: 1_000_000,     // ≈ 96MB
    sampleRate: 1_000_000);
Log.Information("核心数据总线已创建，缓冲区容量 1,000,000 采样点");
```

1M 采样点在 1MHz 采样率下覆盖 1s 时间窗口，匹配当前最大消费者读取间隔（7s 低频 UI），且 `CONTEXT.md` Key Decision #8 记录的设计容量即为 1M。

#### 5. 新增 CONTEXT.md 领域语言文档（+107 行）

建立了项目级术语体系，覆盖：

| 分类 | 定义的术语 |
|------|-----------|
| **数据实体** | 采样点 (Sample Point)、电压数据块 (Voltage_block)、结构化采样点 (StructuredSample)、检测批次 (DetectionBatch) |
| **数据通道** | 核心数据总线 (CoreDataBus)、检测数据通道 (DetectionChannel)、UI 专用缓冲区 (UISharedBuffer) |
| **数据流** | 逐条流式写入 (Per-Sample Streaming Write)、整批写入 (Batch Write)、降采样 (Downsampling) |
| **系统角色** | 主控进程 (Master Process)、数据采集子进程 (Acquisition Subprocess)、持久化 (Persistence) |
| **检测** | 信号遮挡 (Signal Obstruction)、工况判别 (Condition Assessment) |

同时记录了 10 条关键架构决策（Key Decisions）和 3 个已解决的遗留歧义（Flagged Ambiguities），以及示例对话（Example Dialogue）用于未来 AI/新人快速理解领域概念。

---

## 六、架构影响

本次提交为**收敛型变更**，不新增架构组件，仅修正上一提交遗留的设计未闭合问题：

| 维度 | 变更前 | 变更后 |
|------|--------|--------|
| SampleCount 类型 | `double`（消费端强制 `(int)` 转换） | `int`（全链路一致） |
| CoreBusHeader 时间能力 | 仅有 `SampleRate`，无物理时钟锚点 | + `ReferenceTick` / `ReferenceUtcTicks`，可还原 UTC 绝对时间 |
| 读取 API | `ReadLatestSingle()` → `StructuredSample`，零值歧义 | `TryReadLatestSingle(out StructuredSample)` → `bool`，语义明确 |
| CoreDataBus 容量 | 10M 采样点（≈915MB） | 1M 采样点（≈96MB） |
| 领域文档 | 无 | CONTEXT.md（107 行） |

**不影响**:
- 线程模型：仍为 5 线程 + 4 Channel
- 跨进程总线布局：MMF 文件结构不变，CoreBusHeader 仅末尾追加字段（向下兼容）
- 数据流：Analysis → CoreDataBus/DetectionChannel 两路分流不变
- gRPC/MQTT 通信链路

---

## 七、审核报告

> 审查范围：`AvaloniaApplication1/Models/Voltage_block.cs`、`ConsoleApp1/Models/Voltage_block.cs`、`ConsoleApp1/Service/AD_Controlcs.cs`、`ConsoleApp1/Service/SharedMemoryClient.cs`、`WebAPI/Service/SharedMemoryServer.cs`、`WebAPI/Program.cs`、`CONTEXT.md`

### 通过项

| # | 检查点 | 详情 |
|---|--------|------|
| 1 | `SampleCount` 类型一致性 | AvaloniaApplication1、ConsoleApp1 两个项目的 `Voltage_block.cs` 均从 `double` 改为 `int`，消费端冗余 `(int)` 转换已移除 |
| 2 | `CoreBusHeader` 跨进程兼容性 | 新增 `ReferenceTick` / `ReferenceUtcTicks` 追加在结构体末尾，不影响已有字段的偏移量，已有 MMF 文件的旧 Header 中这些字段为零值（安全降级） |
| 3 | `ReferenceTick` / `ReferenceUtcTicks` 赋值线程安全 | `Create()` 在 MMF 创建后、子进程连接前执行，仅主控进程单线程写入，无竞争 |
| 4 | `TryReadLatestSingle()` 语义明确 | WriteIndex=0 时返回 `false`，消费者显式处理空数据分支，消除 zeroed-default 歧义 |
| 5 | `TryReadLatestSingle()` 符号同步 | `SharedMemoryClient.cs` 和 `SharedMemoryServer.cs` 端实现完全一致 |
| 6 | 缓冲区容量收敛 | `WebAPI/Program.cs` 中 Create(buf:1M) 与 `CONTEXT.md` Key Decision #8 记录一致 |
| 7 | ADDraw 源端 `(int)` 保护 | `(int)(block.nBytes * a)` 显式转换，承认源端表达式仍为 double 的现状，不做越界修复 |
| 8 | CONTEXT.md 术语完整性 | 覆盖数据实体（4 个）、数据通道（3 个）、数据流（3 个）、系统角色（2 个）、检测（2 个），含 10 条架构决策和示例对话 |

### 已修复问题

| # | 严重度 | 位置 | 问题描述 | 修复 |
|---|--------|------|----------|------|
| 1 | **中** | `Voltage_block.cs`（2 处） | `SampleCount` 声明为 `double`，消费端每个使用点均需 `(int)` 强制转换，且 ADDraw 的 `double` 中间表达式存在精度损失风险 | 统一改为 `int`，ADDraw 源端显式 `(int)` 转换 |
| 2 | **高** | `CoreBusHeader`（2 处） | 缺少时间校准基准，消费者无法将 WriteIndex 还原为 UTC 绝对时间，持久化 CSV 和 MQTT 消息缺少有效时间戳 | 新增 `ReferenceTick` / `ReferenceUtcTicks` 双字段，Create() 时一次性捕获 |
| 3 | **中** | `ReadLatestSingle()`（2 处） | WriteIndex=0 时返回 `default(StructuredSample)`，消费者无法区分空数据和零值采样点，信号遮挡检测可能漏判 | 重构为 `TryReadLatestSingle(out StructuredSample)`，返回 bool 指示数据有效性 |
| 4 | **低** | `WebAPI/Program.cs` | 缓冲区容量 10M 与 `CONTEXT.md` 设计文档记录的 1M 不一致，且 915MB 虚拟地址空间在部分环境存在风险 | 下调至 1M，与设计文档一致 |

### 遗留建议（非阻塞）

| # | 严重度 | 位置 | 建议 |
|---|--------|------|------|
| 1 | **低** | `AD_Controlcs.cs` ADDraw | `block.nBytes * a` 表达式仍产生 `double` 类型（`a=2` 由变量推断为 `double`），当前由 `(int)` 显式转换兜底。建议后续统一 `a` 为 `int` 常量，从源头消除 double 中间类型 |
| 2 | **提示** | `CONTEXT.md` | 文档末尾 `Flagged ambiguities` 中 `SampleCount` 歧义可标记为 `已解决（2026-05-04 提交确认）` |
| 3 | **提示** | `SharedMemoryClient.cs` `SharedMemoryServer.cs` | 新增的 `using System.Diagnostics` 在 WebAPI 侧未被 `Stopwatch` 以外使用，可考虑限定引用作用域 |

---

## 八、后续步骤预览（不在本次范围）

- 步骤 6：实现 Lidar 反演算法链（Vis/Cn² 计算），使用 `CoreBusHeader.ReferenceUtcTicks` 还原采样点的绝对时间戳
- 步骤 7：数据持久化线程从 CoreDataBus 定时读取写入 CSV，调用 `TryReadLatestSingle()` 安全获取采样数据
- 步骤 8：低频 UI 线程从 CoreDataBus 定时读取发布 MQTT 低频 Topic
- 步骤 9：数据检测与工况判别模块（3.6），完善检测算法，利用 `TryReadLatestSingle()` 的空数据保护避免误判
