# 提交记录

> 生成时间：2026-06-06 00:45
> 仓库：数据采集与检测系统 V2.0
> 分支：`main`

---

## 一、背景（Background）

设备端输出的低频数据（MQTT 发布和 CSV 持久化）中的时间戳存在两个问题：时区为 UTC 零时区而非用户期望的北京时间（偏差 8 小时），以及 `ToUtcDateTime` 中的整数乘法在系统长时间运行后有溢出风险。

### 问题（Problem）

#### 1. 时间戳时区为 UTC

`TimeHelper.ToUtcDateTime` 输出 UTC 零时区时间（`Z` 后缀），对于北京时间的用户来说无法直接使用。前端图表横轴和 CSV 文件时间与实际观测时间不符，偏差 8 小时。

#### 2. long 乘法溢出风险

`ToUtcDateTime` 中的 `elapsedTicks * 10_000_000L` 为 `long` 整数乘法。系统连续运行约 25 小时后，`elapsedTicks` 累积到 ~10^12 级别，乘积超过 `long.MaxValue`（9.2 × 10^18），导致溢出。

---

## 二、解决方案（Solution）

### 整体思路

将 UTC 时间加 8 小时转为北京时间，同时把整数乘法改为 double 中间量避免溢出。

### 具体实施

#### 1. TimeHelper 防溢出修复

```csharp
// 修改前：long 乘法可能溢出
long elapsed100ns = elapsedTicks * 10_000_000L / frequency;
// 修改后：double 中间量安全
long elapsed100ns = (long)((double)elapsedTicks * 10_000_000L / frequency);
```

#### 2. 发布/持久化层转为北京时间

`LowFrequencyPublisher` 和 `PersistenceService` 两处调用方在 `ToUtcDateTime` 返回后加 `.AddHours(8)`，输出格式从 `Z` 改为 `+08:00`。

---

## 三、Git 提交消息

```
fix(Data): CoreDataBus 时间戳转为北京时间并修复溢出风险
```

**正文：**

1. TimeHelper.ToUtcDateTime 乘法改为 double 中间量，避免 long 溢出
2. LowFrequencyPublisher UTC 时间加 8 小时转为北京时间，格式改为 +08:00
3. PersistenceService CSV 持久化时间戳同步转为北京时间

---

## 四、本次提交详情

### 基本信息

| 字段 | 内容 |
|------|------|
| **提交时间** | 2026-06-06 00:45 |
| **作者** | NB11000 |
| **提交哈希** | `<pending>` |
| **基于提交** | `8421b22` — `feat(MQTT): 建立波形专用连接避免 QoS 0 洪流阻塞控制通道，修复重连死锁` |
| **变更统计** | 4 files changed, +58 insertions, -3 deletions |

### 核心变更文件清单

| 状态 | 文件路径 | 变更说明 |
|------|----------|----------|
| 修改 | `ConsoleApp1/Tools/TimeHelper.cs` | 防溢出修复（+1/-1 行） |
| 修改 | `WebAPI/Service/LowFrequencyPublisher.cs` | UTC → 北京时间（+2/-1 行） |
| 修改 | `WebAPI/Service/PersistenceService.cs` | UTC → 北京时间（+2/-1 行） |
| 新建 | `.scratch/beijing-time-fix/issue.md` | PRD 文档（+51 行） |

---

## 五、架构影响

无架构变更。纯数值转换修正。

---

## 六、审核报告

> 审查范围：`TimeHelper.cs`、`LowFrequencyPublisher.cs`、`PersistenceService.cs`

### 通过项

| # | 检查点 | 详情 |
|---|--------|------|
| 1 | 编译通过 | `dotnet build` 0 错误，仅已有 CS8604 警告 |
| 2 | 溢出安全 | double 尾数 53 位，可精确表示 < 10^15 的整数，远大于现实 elapsedTicks |
| 3 | 时区正确 | `.AddHours(8)` 为标准 .NET API，输出格式 `+08:00` 符合 ISO 8601 |
| 4 | 前端兼容 | MQTT JSON 的 `UTC` 字段名不变，仅值变化，不破窗 |

### 遗留建议（非阻塞）

| # | 严重度 | 位置 | 建议 |
|---|--------|------|------|
| 1 | 低 | `TimeHelper.cs` | 时区偏移量可抽取为配置项，未来支持多时区 |

---

## 七、后续步骤预览（不在本次范围）

- 部署后验证 MQTT `lowfreq` 消息中 `utc` 字段显示 `+08:00` 北京时间
- 验证 CSV 文件中时间戳为北京时间
