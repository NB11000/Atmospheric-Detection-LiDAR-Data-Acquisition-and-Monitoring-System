# 提交记录

> 生成时间：2026-06-01 22:30
> 仓库：数据采集与检测系统 V2.0
> 分支：`main`

---

## 一、背景（Background）

PersistenceService 是系统核心采集绑定服务，负责周期性从 CoreDataBus 抽样写入 CSV。该服务历史上零测试覆盖——没有一行测试代码验证其核心行为：启动后正确读写 CSV、多周期追加不覆盖、空总线不创建文件、UTC 时间还原正确、Stop 后停止写入。

同时，WebAPI 侧的 CoreDataBus 仅支持 `Create()` / `Open()` / `TryReadLatestSingle()`，缺少 `Write()` 方法，导致集成测试无法向 MMF 总线写入模拟数据。

### 问题（Problem）

#### 1. PersistenceService 零测试覆盖

作为系统关键数据落盘组件，CSV 格式错误、文件路径问题、UTC 时间偏差等问题只能通过生产运行时发现，无自动化回归手段。

#### 2. WebAPI CoreDataBus 缺少 Write 能力和可配置 mapName

`CoreDataBus` 使用硬编码 `const string MapName = "DAQ_CORE_DATA_BUS"`，多测试并发会冲突。且没有 `Write()` 方法，无法从测试侧写入模拟数据。

---

## 二、解决方案（Solution）

### 整体思路

给 WebAPI `CoreDataBus` 添加 `Write()` 方法和可配置 `mapName` 构造函数，然后编写 3 个集成测试覆盖 PersistenceService 的 5 个核心场景。

### 具体实施

#### 1. CoreDataBus 改造（`SharedMemoryServer.cs`）

- 新增构造函数 `CoreDataBus(string mapName = "DAQ_CORE_DATA_BUS")`，默认参数保证现有 DI 无影响
- 新增 `Write(ref StructuredSample sample)` 方法，与 ConsoleApp1 版一致的 MemoryBarrier 无锁单写者实现
- `Create()` 和 `Open()` 改用 `_mapName` 字段

#### 2. 3 个集成测试（`PersistenceServiceTests.cs`）

| 测试 | 覆盖场景 | 关键断言 |
|------|----------|----------|
| `WriteAndAppendAndStop` | 基础写入 + 多周期追加 + Stop 后停止 | CSV 表头格式、单行数据正确、追加不覆盖、Stop 后不追加 |
| `EmptyBus_NoFileCreated` | 空总线不创建文件 | CoreDataBus 无数据时不产生任何 CSV |
| `UtcConversion_Correct` | UTC 时间还原 | CSV UTC 列 = `TimeHelper.ToUtcDateTime()` 独立计算结果 |

每个测试使用独立 MMF（`Guid.NewGuid()` 命名），临时目录自动清理，`IOptionsMonitor<PersistenceSettings>` 通过 `TestOptions<T>` 适配。

---

## 三、Git 提交消息

```
test(Persistence): 新增 PersistenceService 集成测试，CoreDataBus 添加 Write 支持
```

**正文：**

1. CoreDataBus 新增 mapName 构造函数（默认值兼容）+ Write() 方法
2. 新增 3 个 PersistenceService 集成测试覆盖 5 场景（写入/追加/停止/空总线/UTC）
3. 使用独立 MMF 命名 + 临时目录，测试间完全隔离
4. 全量 WebAPI.Tests（排除预存 Lidar 失败）113/113 通过

---

## 四、本次提交详情

### 基本信息

| 字段 | 内容 |
|------|------|
| **提交时间** | 2026-06-01 22:30:00 |
| **作者** | NB11000 |
| **提交哈希** | `<待生成>` |
| **基于提交** | `489c165` — `fix(ConfigLauncher): 修复关闭启动器窗口死循环，增加三按钮 WebAPI 关闭对话框` (2026-06-01) |
| **变更统计（核心 2 文件）** | 2 files changed, +222 insertions, -3 deletions |

### 核心变更文件清单

| 状态 | 文件路径 | 变更说明 |
|------|----------|----------|
| 修改 | `WebAPI/Service/SharedMemoryServer.cs` | 新增构造函数 + Write() + using System.Threading（+26/-3 行） |
| 新建 | `Test/WebAPI.Tests/PersistenceServiceTests.cs` | 3 个集成测试 + TestOptions 适配器 + 工具方法（+199 行） |

---

## 五、架构影响

| 维度 | 变更前 | 变更后 |
|------|--------|--------|
| CoreDataBus 写入 | 仅 ConsoleApp1 有 Write() | WebAPI CoreDataBus 也有 Write()，两端 API 对齐 |
| MMF 命名 | 硬编码 `"DAQ_CORE_DATA_BUS"` | 可选 `mapName` 参数，默认值兼容 |
| PersistenceService 测试 | 零覆盖 | 3 个集成测试覆盖 5 场景 |

---

## 六、审核报告

> 审查范围：`SharedMemoryServer.cs`、`PersistenceServiceTests.cs`

### 通过项

| # | 检查点 | 详情 |
|---|--------|------|
| 1 | Write() 内存屏障 | `Interlocked.MemoryBarrier()` 在 `WriteIndex++` 之前，与 `TryReadLatestSingle` 的 `Volatile.Read` 配对，保证 happens-before |
| 2 | 默认参数兼容 | `CoreDataBus(string mapName = "DAQ_CORE_DATA_BUS")` — DI `AddSingleton<CoreDataBus>()` 无影响 |
| 3 | 测试隔离 | GUID 独立 MMF 名 + 独立临时目录，无冲突风险 |
| 4 | 资源清理 | `IDisposable` 确保 tempDir 清理，`try/finally` 确保 MMF Dispose |
| 5 | 时间验证 | UTC 断言使用独立 `TimeHelper.ToUtcDateTime()` 计算，非内联重复代码 |

### 遗留建议（非阻塞）

| # | 严重度 | 位置 | 建议 |
|---|--------|------|------|
| 1 | 低 | `PersistenceServiceTests.cs` | 测试使用 `Task.Delay(6500)` 等待周期，可考虑将 `IntervalSeconds` 改为可配置以加速 |
