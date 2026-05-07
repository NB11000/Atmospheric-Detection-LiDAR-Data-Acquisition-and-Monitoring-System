---
Status: needs-triage
Created: 2026-05-07
---

# PRD: 大气检测激光雷达数据反演链路

## Problem Statement

数据采集子进程（ConsoleApp1）当前已经完成了从采集卡原始 ADC 字节到模拟电压值的转换（ADDraw 线程），并将电压数据通过 Analysis 线程填充为 StructuredSample。但 StructuredSample 中的 Vis（能见度）和 Cn2（大气折射率结构常数）字段始终为占位值 0.0。系统缺少从电压信号到物理量的反演算法，导致下游消费者（持久化、低频发布、前端 UI）拿不到有效的激光雷达物理量数据。

## Solution

在 Analysis 线程中嵌入 LidarInverter 反演器，在逐点填充 StructuredSample 的 for 循环之前对整帧电压数据完成反演计算：

- **能见度 Vis**：Fernald (Klett) 后向积分法，单帧瞬时值，整帧共享一个 Vis 值（单位 km）
- **折射率结构常数 Cn²**：闪烁方差法，N=100 帧滑动窗口统计值，逐距离门独立输出 Cn2Profile[]（单位 m⁻²/³）

反演前对电压做三步预处理：每帧自动扣除暗电流、距离平方校正、双通道增益均衡。单通道模式下 Cn² 全部填 -1.0（哨兵值）；双通道模式下前 99 帧窗口不满时 Cn² 填 -1.0，第 100 帧起输出首个有效值。

## User Stories

1. As a 系统操作员, I want 能见度 Vis 数据随采集实时输出到持久化 CSV 文件, so that 我可以回溯分析大气消光状况的历史变化趋势
2. As a 系统操作员, I want Cn² 湍流剖面数据随采集实时输出, so that 我可以评估大气湍流强度对光学传输的影响
3. As a 前端开发者, I want Vis/Cn2 数据通过 CoreDataBus 流式可用, so that 前端 UI 可以通过低频 MQTT 链路订阅并渲染物理量趋势图表
4. As a 数据检测开发者, I want Vis/Cn2 数据通过 DetectionChannel 整批传递, so that 检测线程可以基于物理量阈值触发工况告警
5. As a 测试工程师, I want LidarInverter 可以用合成电压数据独立测试, so that 反演算法的正确性可以被自动化验证而不依赖真实采集卡硬件
6. As a 设备配置管理员, I want 算法参数（K 常数、接收孔径、窗口大小）可以通过配置文件调整, so that 设备标定和参数优化不需要重新编译代码
7. As a 系统集成者, I want 单通道模式下系统正常运行不崩溃（Cn² 输出 -1.0）, so that 单通道采集场景不会因缺少算法支持而中断
8. As a 运维人员, I want 反演模块初始化期间的帧（前 99 帧）明确输出 -1.0 哨兵值, so that 下游消费者可以区分"未就绪"和"真实值为 0"两种状态

## Implementation Decisions

### 模块划分

**LidarInverter（核心反演器）**：
单一入口 `Invert(Voltage_block voltageBlock, byte chSel)` 返回 `(double vis, double[] cn2Profile)` 元组。内部维护 Cn² 滑动窗口环形缓冲区（N=100 帧），持有校准后双通道电压历史。处理流程：暗电流扣除 → 距离平方校正 → 增益均衡 → Fernald Vis 计算 → 闪烁方差 Cn² 计算。所有数学运算使用 `Span<T>` + `stackalloc` 避免堆分配。

**LidarAlgorithmConfig（配置模型）**：
包含双通道增益均衡系数、K 常数（默认 4.48）、接收孔径 D、路径长度 L、滑动窗口大小 N（默认 100）、Fernald 远端边界条件参数。从 DeviceConfig.json 的 `LidarAlgorithm` 节读取。

### 架构决策

- LidarInverter 作为 AD_Controlcs 的 DI 注入依赖，构造函数从 ConfigHelper 读取配置
- Analysis 线程在现有 `for (int i = 0; i < count; i++)` 循环之前调用 `_lidarInverter.Invert()`
- 循环内 `detArr[i].Vis = vis`（整帧共享同一 Vis 值）、`detArr[i].Cn2 = cn2[i]`（逐距离门独立值）
- CoreDataBus 和 DetectionChannel 的现有写入流程不做任何修改
- 预处理嵌入 LidarInverter 内部，不暴露为独立公共类

### 算法决策

- Vis 使用 Fernald 后向积分（Klett 法），从远端边界点向近端积分，边界点假设 α_a(r_max) ≈ 0（洁净大气）
- Cn² 使用标准球面波形式：`Cn² = K × σI² × D^(7/3) × L^(-11/6)`，K 默认 4.48 可配置
- 暗电流每帧从"无激光时段"末尾自动采集取均值
- 双通道增益均衡系数出厂标定，配置文件读取，运行时不更新
- 单通道模式下 Cn² 所有距离门填 -1.0
- 双通道模式下帧号 1~99 的 Cn² 填 -1.0，帧号 100 起输出有效值，之后滑动窗口每帧更新

### 配置 Schema 变更

DeviceConfig.json 新增 `LidarAlgorithm` 节：
- `GainEqualizationCoefficient`（double，双通道增益均衡系数）
- `KConstant`（double，默认 4.48）
- `ReceiverApertureD_m`（double，接收孔径，单位 m）
- `PathLengthL_m`（double，路径长度，单位 m）
- `Cn2WindowFrames`（int，默认 100）
- `FernaldBoundaryDistance_m`（double，远端边界距离，单位 m）

## Testing Decisions

### 测试策略

只测试 LidarInverter 的公共接口行为，不测试内部实现细节。用合成电压数据（已知 Vis/Cn² 真值）验证输出偏离度 <5%。测试覆盖三种通道模式（单 CH1 / 单 CH2 / 双通道）、边界条件（空帧、窗口不满）和哨兵值输出。

### 测试模块

- **LidarInverterTests**：端到端反演测试（合成电压 → Vis + Cn2Profile），覆盖：
  - Fernald 后向积分 Vis 计算正确性
  - Cn² 闪烁方差法输出正确性
  - 单通道模式 Cn² = -1.0
  - 双通道模式前 99 帧 Cn² = -1.0
  - 第 100 帧起 Cn² 输出有效值
  - 滑动窗口更新后 Cn² 值变化
  - 暗电流扣除效果验证
  - 距离平方校正效果验证

### 不测试的内容

- 不对内部私有方法做单元测试
- 不测试 Analysis 线程的 Channel 读写逻辑（已有现有测试覆盖）

## Out of Scope

- MQTT 告警 Topic 发布（`daq/{id}/lidar/alarm`）——后续独立 PRD
- 数据检测与工况判别模块（SignalQualityDetector / ConditionAssessor）——后续独立 PRD
- WebAPI 端 LidarDetectionService ——后续独立 PRD
- 前端 UI 的 Vis/Cn2 图表渲染 ——后续独立 PRD
- 气象因子（温/湿/压/风速/风向/降雨）的传感器集成
- 光子计数反演（模拟检测模式不需要）
- 消光系数剖面的独立发布（当前仅输出 Vis 单值）

## Further Notes

- 实施计划文档（`大气检测激光雷达数据解析链路 — 实施计划.md`）中描述的反演算法链（VoltageNonlinearCorrector → PhotonCountInverter → ExtinctionBackscatterInverter → VisibilityCalculator → Cn2ProfileInverter）已被此 PRD 替代为更精简的两算法方案（Fernald + 闪烁方差法）
- 暗电流自动采集依赖于"无激光时段"能被可靠识别——如果采集卡数据流中不存在无激光时段，需要新增采集同步信号
- Cn² 初始化延迟约 4.6 秒（100 帧 × ~46ms/帧），在此期间结构化采样点 Cn² = -1.0
