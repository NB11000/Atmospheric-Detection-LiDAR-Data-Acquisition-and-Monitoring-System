# Implementation Plan: 激光雷达数据反演链路

> Parent: [PRD: 大气检测激光雷达数据反演链路](../PRD.md)
> Created: 2026-05-07

## Modules

| 模块 | 位置 | 单一职责 |
|------|------|---------|
| LidarAlgorithmConfig | `ConsoleApp1/Models/` | 反演算法配置参数 POCO，从 JSON 反序列化 |
| LidarInverter | `ConsoleApp1/Service/` | 核心反演器：预处理 + Fernald Vis + 闪烁方差 Cn²，维护滑动窗口状态 |
| AD_Controlcs (Analysis) | `ConsoleApp1/Service/` | 调用 LidarInverter.Invert()，将结果填入 StructuredSample |
| ConfigHelper | `ConsoleApp1/Tools/` | 新增 LidarAlgorithm 配置节读取 |
| DeviceConfig.json | `ConsoleApp1/Config/` & `WebAPI/Config/` | 新增 LidarAlgorithm 配置节 |

## Interfaces

### LidarAlgorithmConfig

```
class LidarAlgorithmConfig {
    double GainEqualizationCoefficient  // 双通道增益均衡系数
    double KConstant                    // Cn² 公式 K 常数，默认 4.48
    double ReceiverApertureD_m          // 接收孔径 (m)
    double PathLengthL_m                // 路径长度 (m)
    int Cn2WindowFrames                 // Cn² 滑动窗口大小，默认 100
    double FernaldBoundaryDistance_m    // Fernald 远端边界距离 (m)
    double LaserWavelength_nm           // 激光波长 (nm)，Angstrom 修正用
    double AngstromExponent             // Angstrom 指数，默认 1.3
    int DarkCurrentSampleCount          // 暗电流采样点数（无激光段末尾）
}
```

### LidarInverter

```
class LidarInverter {
    // 构造函数：从 LidarAlgorithmConfig 初始化，分配 Cn² 环形缓冲区
    LidarInverter(LidarAlgorithmConfig config)
    
    // 单帧反演入口
    // voltageBlock: 上游 ADDraw 产出的电压块
    // chSel: 通道选择 (1=CH1, 2=CH2, 3=双通道)
    // 返回: (vis 整帧能见度km, cn2Profile 逐距离门Cn²剖面)
    (double vis, double[] cn2Profile) Invert(Voltage_block voltageBlock, byte chSel)
    
    // 内部状态
    - CircularBuffer<double[]> _ch1History   // CH1 最近 N 帧校准后电压
    - CircularBuffer<double[]> _ch2History   // CH2 最近 N 帧校准后电压
    - int _frameCount                         // 已处理帧数
    - LidarAlgorithmConfig _config
}
```

### AD_Controlcs.Analysis（修改）

现有 Analysis 方法（AD_Controlcs.cs:750-827）的逐点循环之前插入：

```
var (vis, cn2Profile) = _lidarInverter.Invert(voltageBlock, (byte)(_captureCardConfig.SyncChannelIndex + 1));
// 然后在 for 循环中:
detArr[i].Vis = vis;
detArr[i].Cn2 = cn2Profile[i];
```

## Data Flow

### Happy Path

```
ADDraw → Voltage_block
  → Analysis 消费
    → LidarInverter.Invert(voltageBlock, chSel)
      → [1] 暗电流扣除 (从帧尾取均值)
      → [2] 距离平方校正 V[i] *= r[i]²
      → [3] 双通道增益均衡 (CH2 *= coeff)
      → [4] Fernald 后向积分 → 整帧 Vis
      → [5] 写入环形缓冲区, 若满 → 计算每个距离门的 σI² → Cn²
      → 返回 (vis, cn2Profile[])
    → for i in 0..count:
        detArr[i].Vis = vis
        detArr[i].Cn2 = cn2Profile[i]
        _coreBus.Write(ref detArr[i])
    → DetectionCh.TryWrite(detArr)
```

### Error Path

```
LidarInverter.Invert() 内部异常:
  → try-catch 包裹，异常帧 Vis = -1.0, Cn2 = -1.0（全无效）
  → 不中断 Analysis 线程循环
  → _logger.LogError 记录异常详情
```

### Edge Cases

| 场景 | 行为 |
|------|------|
| 单通道模式 (chSel=1/2) | Vis = Fernald(对应通道), Cn2 = -1.0 全帧 |
| 双通道前 99 帧 | Vis 正常输出, Cn2 = -1.0 全帧 |
| 双通道第 100 帧起 | Vis + Cn2 均正常输出 |
| 电压全零帧 | 暗电流扣除后全零 → Vis = -1.0, Cn2 = -1.0 |
| 暗电流窗口超出帧范围 | 使用整个帧均值作为暗电流 |
| 某距离门电压 ≤ 0 | skip，对应门 Cn2 = -1.0 |

## Key Technical Decisions

| 决策 | 选择 | 原因 | 替代方案被拒 |
|------|------|------|------------|
| Cn² 哨兵值 | -1.0 | 物理上 Cn² 恒正，-1 天然与有效值区分 | NaN（会传播破坏下游），0.0（歧义）|
| Fernald 边界 | r_max 处 α_a = 0（洁净大气假设） | 标准做法，最远端气溶胶消光远小于分子消光 | 对向积分（近→远，不稳定）|
| Cn² 公式形式 | 球面波 | 大气激光雷达发射光束发散角导致有效波前为球面 | 平面波（仅适用准直光束，不符合你的光学系统）|
| 滑动窗口 | 环形缓冲区，每帧一个完整 double[] | 帧间隔 ~46ms，copy 开销可忽略，实现简单 | 逐点队列（碎片化，跨距离门管理复杂）|
| 预处理位置 | LidarInverter 内部私有方法 | 预处理是反演的内部步骤，不应独立暴露 | 独立 LidarPreprocessor 类（暴露增加耦合面）|
| 缓存策略 | 无缓存，每帧全量重算 | 配置参数可能热更新，缓存需额外失效逻辑 | 缓存 Fernald 距离校正（配置变更时可能出错）|

## Test Strategy

### LidarInverterTests（核心测试类）

| 测试方法 | 类型 | 测试内容 |
|---------|------|---------|
| `Fernald_UniformAtmosphere_ReturnsCorrectVis` | 单元 | 合成均匀消光剖面，Vis 与理论值偏差 <5% |
| `Fernald_Ch2Only_UsesChannel2` | 单元 | 单通道 CH2 模式下 Vis 正确 |
| `Cn2_SingleChannel_ReturnsNegativeOne` | 单元 | 单通道所有 Cn2 = -1.0 |
| `Cn2_WindowNotFull_ReturnsNegativeOne` | 单元 | 前 99 帧 Cn2 全 -1.0 |
| `Cn2_Frame100_ReturnsValidValues` | 单元 | 第 100 帧 Cn2 > 0 |
| `Cn2_SlidingWindow_UpdatesCorrectly` | 单元 | 第 101 帧用最新 100 帧窗口 |
| `DarkCurrent_SubtractsCorrectly` | 单元 | 已知暗电流 + 已知信号 → 扣除后正确 |
| `RangeCorrection_AppliesCorrectly` | 单元 | 距离平方校正的数值验证 |
| `EmptyVoltageFrame_ReturnsSentinelValues` | 单元 | 全零电压 → Vis=-1, Cn2=-1 |
| `GainEqualization_BalancesChannels` | 单元 | 增益均衡后两通道均值比 ≈ 1.0 |

**测试原则**：只通过 Invert() 公共接口验证行为。不测试内部 private 方法。不 mock 配置（直接用测试配置对象）。合成数据使用标准 Lidar 方程生成已知真值。

### 不写的测试

- Fernald 内部滑动窗口拟合的中间步骤（实现细节）
- Analysis 线程 Channel 消费逻辑（已有现有测试）
- ConfigHelper JSON 反序列化（框架行为）
- 性能基准测试（非功能需求，手工验证）

## Vertical Slice Design

### Slice 1: 配置模型 + 配置读取
**依赖**: 无  
**模块**: LidarAlgorithmConfig (新建), ConfigHelper (修改), DeviceConfig.json (修改)  
**可测试**: 配置反序列化通过 dotnet run 验证

### Slice 2: LidarInverter — Fernald Vis（单通道）
**依赖**: Slice 1  
**模块**: LidarInverter (新建, 仅 Vis 功能)  
**可测试**: LidarInverterTests.Fernald_* 测试

### Slice 3: LidarInverter — Cn² 闪烁方差（双通道）
**依赖**: Slice 2  
**模块**: LidarInverter (新增 Cn² 功能 + 环形缓冲区)  
**可测试**: LidarInverterTests.Cn2_* 测试

### Slice 4: 预处理（暗电流 + 距离校正 + 增益均衡）
**依赖**: Slice 2  
**模块**: LidarInverter (新增预处理)  
**可测试**: LidarInverterTests.DarkCurrent_* / RangeCorrection_* / GainEqualization_*

### Slice 5: 嵌入 Analysis 线程
**依赖**: Slice 3, Slice 4  
**模块**: AD_Controlcs.Analysis (修改)  
**可测试**: 端到端 — 启动采集，持久化 CSV 中 Vis/Cn2 不为零值
