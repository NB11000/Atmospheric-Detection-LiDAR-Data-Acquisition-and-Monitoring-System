# 高频数据采集与分发系统 V2.0

从 USB1602 采集卡以 1MHz 采样率读取双通道模拟信号，经结构化转换后通过 MQTT 三层 Topic 分发至远程前端。

## Language

### 数据实体

**采样点 (Sample Point)**：
采集卡在单个采样时刻对一个通道捕获的电压值（16-bit ADC）。
_Avoid_: 数据点、数据条目

**电压数据块 (Voltage_block)**：
一帧原始电压数据，包含 CH1/CH2 的 `double[]` 数组和该帧的起始时间戳。
_Avoid_: 数据帧、原始块

**结构化采样点 (StructuredSample)**：
12 字段（Timestamp / Time / CH1 / CH2 / Vis / Cn2 / 6 个气象字段）、96 字节的标准化数据结构，是系统内部唯一数据货币。
_Avoid_: 结构化数据、样本

**检测批次 (DetectionBatch)**：
从 ArrayPool 租用的 `StructuredSample[]` + 实际采样点数 `Count` 的包装，通过 DetectionChannel 传递。不依赖数组 `.Length` 获取真实数据量。

### 数据通道

**核心数据总线 (CoreDataBus)**：
基于 MMF 的扁平环形缓冲区，单写（Analysis）多读（持久化/低频UI）。数据区为 `StructuredSample[BufferLength]`（1M 采样点，~96MB），WriteIndex 单调递增永不回绕。
_Avoid_: 环形缓冲区、共享内存

**检测数据通道 (DetectionChannel)**：
同进程 `Channel<DetectionBatch>`，Analysis 整批写入、Detection 线程整批消费，与 CoreDataBus 物理隔离。
_Avoid_: 检测通道、数据通道

**UI 专用缓冲区 (UISharedBuffer)**：
独立 MMF 环形缓冲区，仅服务高频实时链路（降采样波形），与 CoreDataBus 不同文件、不同数据源。
_Avoid_: UI 缓冲区

### 数据流

**逐条流式写入 (Per-Sample Streaming Write)**：
Analysis 每填充完一个 StructuredSample 立即调用 `CoreDataBus.Write()`，WriteIndex 逐 1 推进。消费者无需等待整帧处理完毕即可读取。
_Avoid_: 实时写入、单点写入

**整批写入 (Batch Write)**：
Analysis 处理完整个 Voltage_block 后，从 ArrayPool 租用数组、拷贝全帧 StructuredSample，一次性 `TryWrite` 到 DetectionChannel。

**降采样 (Downsampling)**：
UI 刷新线程对原始电压值取分段 min/max（降采样比 1000），写入 UISharedBuffer 供主控进程发布。

### 系统角色

**主控进程 (Master Process)**：
WebAPI（ASP.NET Core），负责创建 CoreDataBus / UISharedBuffer、托管 MQTT 客户端、通过 gRPC 双向流接收子进程告警。
_Avoid_: Web 服务、API 进程

**数据采集子进程 (Acquisition Subprocess)**：
ConsoleApp1，直接驱动 USB1602 采集卡，运行 5 条线程（ADWork / ADDraw / Analysis / Detection / UI），通过 gRPC 双向流与主控进程通信。

**持久化 (Persistence)**：
从 CoreDataBus 按配置周期（1s / 5s / 30s / 1min / 5min）读取**单条**最新结构化采样点，写入 CSV。本质是周期性快照抽样，非全量归档。
_Avoid_: 存档、落盘

### 检测

**信号遮挡 (Signal Obstruction)**：
CH1 / CH2 通道电压值持续接近零（< ±0.01），判为光路被物理遮挡。

**工况判别 (Condition Assessment)**：
对结构化采样点执行阈值比较、波形畸变检测、跳变识别，输出工况等级与告警。

## Relationships

- **ADDraw** 产出 `Voltage_block` → **Analysis** 消费，转换为 `StructuredSample` → 分两路：**CoreDataBus**（逐条流式）和 **DetectionChannel**（整批）
- **CoreDataBus** 仅服务两个消费者：**持久化线程** 和 **低频 UI 线程**，各自按定时调用 `TryReadLatestSingle()`
- **DetectionChannel** 服务 **检测线程**，与 CoreDataBus 物理隔离
- **UISharedBuffer** 仅服务 **高频实时链路**：UI 刷新线程降采样写入 → 主控进程读取发布 MQTT
- **检测线程** 发现异常 → `GrpcClient.SendErrorMessage()` → gRPC 双向流 → **主控进程** → MQTT 告警 Topic

## Key Decisions

1. **扁平环形数组**而非分槽块状结构：Analysis 线程消费间隔 ~50ms，块内写入微秒级，扁平结构消除槽边界延迟
2. **WriteIndex 永不回绕**：用 `long` 单调递增，取模得真实位置，消费者无需追踪回绕边界
3. **时间戳线性推算**：`sample[i].Time = startTick + i * ticksPerSample`，基于 Stopwatch.GetTimestamp()。无硬件时戳可用
4. **时间校准基准**：`CoreBusHeader` 存 `ReferenceTick` + `ReferenceUtcTicks`，消费者据此还原绝对 UTC 时间，跨平台兼容
5. **持久化 = 快照抽样**：不保存全量数据，每个周期取一条最新值
6. **CoreDataBus.Write() 用 MemoryBarrier**：写数据 → 全屏障 → 推进 WriteIndex，消费者 `Volatile.Read` 配对，保证 happens-before
7. **DetectionBatch 包装**：解决 `ArrayPool.Rent` 返回长度 ≥ 实际 count 的歧义
8. **缓冲区 1M 采样点**：窗口 ~1s（1MHz），覆盖消费者读取耗时的 3 个数量级抖动
9. **生产消费者生命周期绑定**：生产者退出时消费者同步退出，不会孤立消费旧数据
10. **CoreDataBus 与 UISharedBuffer 完全物理隔离**：不同 MMF 文件，不同数据源，不同消费链路

## Example dialogue

> **Dev:** "持久化线程 5 分钟读一次，但缓冲区只有 1 秒窗口——中间 299 秒数据全丢了吗？"
> **Domain expert:** "全丢了。持久化就是周期性工况快照，不是全量归档。数据源头 1MHz 的原始采样量不可能也不需要在 MQTT 链路上全量保留。"
>
> **Dev:** "检测线程能读到 CoreDataBus 吗？"
> **Domain expert:** "不读。检测线程通过 DetectionChannel 直接拿整帧 StructuredSample[]，同进程 Channel 延迟 < 1μs，比跨进程 MMF 更快。"
>
> **Dev:** "子进程 crash 重启后 Timestamp 从 0 开始，消费者怎么判断新旧？"
> **Domain expert:** "Timestamp 只是 session-local 递增序号，不做跨进程排序。全局排序靠 `Time` 字段还原出的 UTC 绝对时间。"

## Flagged ambiguities

- "SampleRate" 曾以 Hz 和 kHz 两种单位出现——已解决：配置值存 kHz，CoreBusHeader 存 Hz（×1000），代码已统一
- "持久化" 曾被理解为全量归档——已解决：即周期性抽样
- `Voltage_block.SampleCount` 曾是 `double`——已改为 `int`
