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

### 生命周期

**采集绑定服务 (Acquisition-Bound Service)**：
运行生命周期与采集状态绑定的消费者——采集开始时启动内部循环，采集停止时停止。当前包括波形发布、低频发布、持久化三类，由采集生命周期协调器统一启停。
_Avoid_: 后台服务、托管服务

**采集生命周期协调器 (Acquisition Lifecycle Coordinator)**：
订阅采集状态变化和 MQTT 连接状态变化两个事件，根据各采集绑定服务声明的前置条件（`RequiresMqttConnection`）决定启动或停止。每一个采集绑定服务的启动条件：`CanRun = Acquiring && (!RequiresMqtt || MqttConnected)`。
_Avoid_: 事件总线、调度器

### 检测

**信号遮挡 (Signal Obstruction)**：
CH1 / CH2 通道电压值持续接近零（< ±0.01），判为光路被物理遮挡。

**工况判别 (Condition Assessment)**：
对结构化采样点执行阈值比较、波形畸变检测、跳变识别，输出工况等级与告警。

**检测告警 (Detection Alert)**：
检测线程发现异常时产生的结构化告警，包含 AlarmType / Severity / Timestamp / CH1 / CH2，经 gRPC `"Detection"` 消息类型上报至主控进程，由 DetectionPublisherService 发布到专属 MQTT topic `daq/{id}/detection/alerts`。
_Avoid_: 错误消息、Error 事件

## Relationships

- **ADDraw** 产出 `Voltage_block` → **Analysis** 消费，转换为 `StructuredSample` → 分两路：**CoreDataBus**（逐条流式）和 **DetectionChannel**（整批）
- **CoreDataBus** 服务三个消费者：**持久化服务**、**低频发布服务**（主控进程）和 **低频 UI 线程**（子进程），各自按定时调用 `TryReadLatestSingle()`
- **DetectionChannel** 服务 **检测线程**，与 CoreDataBus 物理隔离
- **UISharedBuffer** 仅服务 **高频实时链路**：UI 刷新线程降采样写入 → 主控进程读取发布 MQTT
- **检测线程** 发现异常 → `GrpcClient.SendDetectionMessage()`（message_type: `"Detection"`）→ gRPC 双向流 → **GrpcServiceImpl** 路由至 **DetectionPublisherService** → MQTT `daq/{id}/detection/alerts`
- **采集生命周期协调器** 订阅 `AcquiringStateChanged` + `MqttConnectionStateChanged` → 按需调 **采集绑定服务** 的 Start/Stop
- **波形发布服务**、**低频发布服务**、**检测发布服务** 需 MQTT 连接才可运行；**持久化服务** 仅需采集状态

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
11. **消费者生命周期由事件驱动，不由信号驱动**：消费者是抽样快照（7s ~ 5min 周期），非全量消费，跨进程信号唤醒每秒百万次无意义；采集状态和 MQTT 连接状态统一由 SystemStateService 发布，Coordinator 集中判断分发
12. **采集绑定服务不继承 BackgroundService**：纯 Singleton，Start/Stop 由 Coordinator 调用，各自管理 CTS 和 Dispose，避免 ExecuteAsync 空实现的语义误导

## Example dialogue

> **Dev:** "持久化线程 5 分钟读一次，但缓冲区只有 1 秒窗口——中间 299 秒数据全丢了吗？"
> **Domain expert:** "全丢了。持久化就是周期性工况快照，不是全量归档。数据源头 1MHz 的原始采样量不可能也不需要在 MQTT 链路上全量保留。"
>
> **Dev:** "检测线程能读到 CoreDataBus 吗？"
> **Domain expert:** "不读。检测线程通过 DetectionChannel 直接拿整帧 StructuredSample[]，同进程 Channel 延迟 < 1μs，比跨进程 MMF 更快。"
>
> **Dev:** "子进程 crash 重启后 Timestamp 从 0 开始，消费者怎么判断新旧？"
> **Domain expert:** "Timestamp 只是 session-local 递增序号，不做跨进程排序。全局排序靠 `Time` 字段还原出的 UTC 绝对时间。"
>
> **Dev:** "采集停止后持久化服务还在写 CSV，怎么停掉？"
> **Domain expert:** "持久化是采集绑定服务，采集停止时采集生命周期协调器自动调 Stop，内部循环退出。没必要等采集停止后还去读总线。"

## LiDAR 反演

### 预处理

**暗电流扣除 (Dark Current Subtraction)**：
每帧"无激光时段"末尾取均值作为暗电流，从整帧电压中扣除。运行时自动采集，不静态配置。
_Avoid_: DC offset、偏置电压

**距离平方校正 (Range-Squared Correction)**：
`V_corr(r) = V_raw(r) × r²`，其中 r 由采样率和光速换算。修正 Lidar 回波信号的几何衰减。
_Avoid_: 几何衰减校正

**双通道增益均衡 (Gain Equalization)**：
出厂一次性标定系数，配置文件静态读取。将 CH1/CH2 平均亮度拉平，消除接收光路和采集卡增益差异。运行时不更新。

### 算法

**能见度 (Vis)**：
单帧瞬时值，Fernald (Klett) 后向积分法，单通道即可算。单位 km，保留 3 位小数。整帧共享一个 Vis 值（非逐距离门）。

**折射率结构常数 (Cn²)**：
最近 N=100 帧滑动窗口统计值，闪烁方差法，必须双通道。单位 m⁻²/³。逐距离门独立输出 Cn2Profile[]。前 99 帧窗口不满时，Cn² 填 -1.0（哨兵值，语义=无效）。单通道模式下所有帧 Cn² 均为 -1.0。转换公式为标准球面波形式：`Cn² = K × σI² × D^(7/3) × L^(-11/6)`，K 默认 4.48 可配置。

### 反演器 (LidarInverter)

`ConsoleApp1/Service/LidarInverter.cs`，暴露 `Invert(Voltage_block, chSel) → (double vis, double[] cn2Profile)`。
Analysis 线程在逐点填充 StructuredSample 的 for 循环之前调用。
构造函数从配置读取：双通道增益均衡系数、暗电流窗口参数、K 常数、D（接收孔径）、L（路径长度）、滑动窗口大小 N。

---

### 测试与模拟

**模拟模式 (Mock Mode)**：
通过 `--mock` 命令行标志进入的特殊运行模式，硬件采集被假数据生成器替代，健康端点激活。主控和子进程均需支持。
_Avoid_: 测试模式、模拟测试、dry-run

**模拟数据生成器 (Mock Data Generator)**：
替代 ADWork 线程，按配置生成合成 `Data_Block`（模拟 USB1602 输出格式），受同一 `ADDataTest_RunFlag` 和 `cts` 控制启停。可注入异常模式（零值段、饱和、通道掉线、时间戳跳跃）。
_Avoid_: 假数据源、虚拟采集卡

**数据异常模式 (Anomaly Pattern)**：
- **zero_segment**：全零数据段（CH1=CH2=0），模拟信号遮挡或硬件故障
- **saturated**：通道全 65535，模拟 ADC 饱和
- **channel_dropout**：单通道全零、另一通道正常，模拟单通道掉线
- **timestamp_gap**：跳过一批采样点，模拟数据帧断裂
- **usb_disconnect**：停止写入 channel N 秒后恢复，模拟 USB 线缆松动

**健康端点 (Health Endpoint)**：
子进程在 `--mock` 下通过 `HttpListener` 暴露的 HTTP 端点（固定端口 19999），返回 JSON 格式的内部指标：线程运行状态、channel 当前队列长度、采样计数、DropOldest 触发次数、内存（WorkingSet）、GC 代龄。
_Avoid_: 监控端口、诊断端口

**SimulationRunner**：
独立 .NET 控制台项目，按场景 JSON 文件编排 S/M/L 三个阶段，启动 WebAPI → WebAPI 自动 spawn 子进程，通过 REST API + MQTT 订阅 + 子进程健康端点三方观测，自动断言并生成 JSON 报告。
_Avoid_: 测试脚本、集成测试项目

**场景文件 (Scenario File)**：
JSON 文件定义测试阶段的时长、检查点列表、异常注入规则（顺序精确时间线或随机区间）。每个检查点包含触发时间和断言类型。
_Avoid_: 测试配置、参数文件

**检查点 (Checkpoint)**：
场景中定时触发的断言，如 `grpc_connected`、`threads_all_running`、`mmf_write_index_advancing`、`mqtt_waveform_publishing`、`memory_stable`、`gc_stable` 等。验证目标状态是否达成，未达成时记录失败但不终止测试（除非配置为 fatal）。
_Avoid_: 断言点、验证点

**测试阶段 (Test Phase)**：
- **S 阶段 (Smoke)**：30 秒，单次启停，验证全链路跑通，注入固定时间线异常
- **M 阶段 (Sustain)**：10 分钟，持续采集，验证内存/GC 稳定性 + MQTT 断连重连，随机区间注入异常
- **L 阶段 (Soak)**：2 小时以上，高强度随机异常注入，验证句柄稳定性、长期自愈合能力
_Avoid_: 短期测试、中期测试、长期测试

## Flagged ambiguities

- "SampleRate" 曾以 Hz 和 kHz 两种单位出现——已解决：配置值存 kHz，CoreBusHeader 存 Hz（×1000），代码已统一
- "持久化" 曾被理解为全量归档——已解决：即周期性抽样
- `Voltage_block.SampleCount` 曾是 `double`——已改为 `int`
- Vis 是整帧单值（整帧共享），Cn2 是逐距离门值（Cn2Profile[]）——已解决：两种不同粒度，均在 StructuredSample 中体现
- Cn² 前 99 帧输出 -1.0 哨兵值，消费者需检查 `if (sample.Cn2 < 0) skip`
