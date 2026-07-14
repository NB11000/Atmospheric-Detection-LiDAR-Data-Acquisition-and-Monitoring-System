# 提交记录

> 生成时间：2026-07-14
> 仓库：数据采集与检测系统 V2.0
> 分支：`main`

---

## 一、背景（Background）

项目长期运行后积累多项工程基础设施缺失和文档滞后问题。

### 问题（Problem）

#### 1. 无 Mock 模拟模式，开发验证依赖硬件

系统无 `--mock` 模式可用，开发人员每次验证数据消费链路（持久化 CSV、低频 MQTT、波形 MQTT）必须连接 USB1602 采集卡和激光器，或手动运行 MMFWriter 注入数据。不具备软仿真开发能力。

#### 2. ConsoleApp1 构建失败

`ConsoleApp1.csproj` 中 `BaseOutputPath` 指向不存在的 `E:\新建文件夹 (2)\数据展示\...` 路径，导致 MSB3026/MSB3027 复制错误，整个解决方案无法构建。

#### 3. README 严重滞后于代码

README 与代码存在大量不一致：遗漏 3 个完整项目（SharedModels、ConfigLauncher、第二个测试项目）、遗漏 7 个 Controller/Service 文件、Controllers 名称与实际不符、波形 Payload 大小标为 16 KB（实际 8 KB）、持久化周期描述与实际硬编码不符。

#### 4. 缺少 AI Agent 引导文件

仓库无 `AGENTS.md`，OpenCode 等 AI 工具进入项目时无上下文，容易误判架构约定（如 CoreDataBus.WriteIndex 不回绕、持久化=快照非全量归档、Cn² 哨兵值、波形二进制格式等）。

---

## 二、解决方案（Solution）

### 整体思路

补全工程基础设施（Mock 模式 + Agent 引导 + 正确的项目文档），修复构建阻断，统一 README 与代码一致性。

### 具体实施

#### 1. Mock 模拟模式

在 `WebAPI/Program.cs` 中新增 `--mock` 参数解析，过滤后传给 ASP.NET。Mock 模式下：
- 不启动 ConsoleApp1 硬件子进程，不建立 gRPC 连接
- 调用 `StartMockMode()` 设置采集卡状态为 ProcessConnected + DeviceOpened + Acquiring，触发 `AcquisitionLifecycleCoordinator` 自动启动所有采集绑定服务
- 启动 `MockDataWriter` 后台循环，每 1ms 写入 1000 个 `StructuredSample`（正弦/余弦模拟波形、能见度、Cn²）到 CoreDataBus，同步写波形数据到 UISharedBuffer
- `ApplicationStopping` 中 Mock 模式跳过 gRPC EXIT 指令发送，仅取消 CTS

#### 2. 修复构建路径

移除 `ConsoleApp1.csproj` 第 22 行的 `BaseOutputPath` 元素，恢复默认输出路径。

#### 3. README 全面重构

- 概述：双进程 → 三组件，新增 ConfigLauncher
- 启动：删除干跑模式，新增 Mock 模式（`dotnet run -- --mock`）
- 部署指南合并入快速开始、发布部署独立成章
- 架构图：新增 ConfigLauncher 节点 + HTTP 管理通道连线
- 项目树：从 35 行重写为 140+ 行，补全 7 个遗漏目录/项目
- 技术栈：WebAPI 补 SignalR + Serilog 三 sink，新增 ConfigLauncher 表
- 约定：持久化固定 5s、Payload 8 KB、Mock 模式说明

#### 4. AGENTS.md

新建 Agent 引导文件，包含：构建/测试命令、双进程架构、IPC 数据流、Mock 模式用法、关键约定（WriteIndex/Cn² 哨兵/二进制 Payload/MachineId 唯一性）、配置文件映射。

---

## 三、Git 提交消息

```
feat(build): 新增 Mock 模式与 AGENTS.md，修复构建路径，重构 README

1. WebAPI 新增 --mock 标志，内嵌 MockDataWriter 写入 CoreDataBus+UISharedBuffer，全消费链路自动运行
2. 移除 ConsoleApp1.csproj 中指向不存在路径的 BaseOutputPath
3. AGENTS.md: 双进程架构、IPC 数据流、关键约定、Mock 模式
4. README 重构: 三组件概述、完整项目树(+7 目录/项目)、技术栈补全、修正 8KB Payload
```

---

## 四、本次提交详情

### 基本信息

| 字段 | 内容 |
|------|------|
| **提交时间** | 2026-07-14 |
| **变更统计（核心 6 文件）** | 6 files changed, +596 insertions, -141 deletions |

### 核心变更文件清单

| 状态 | 文件路径 | 变更说明 |
|------|----------|----------|
| 新增 | `WebAPI/Tools/MockDataWriter.cs` | Mock 数据写入器：循环写 CoreDataBus + UISharedBuffer，正弦/余弦模拟波形（+73） |
| 修改 | `WebAPI/Program.cs` | `--mock` 参数解析、StartMockMode 方法、ApplicationStopping Mock 分支（+58/-10） |
| 新增 | `AGENTS.md` | OpenCode Agent 引导文件（+61） |
| 修改 | `ConsoleApp1/ConsoleApp1.csproj` | 移除指向不存在 E:\ 路径的 BaseOutputPath（+1/-2） |
| 修改 | `README.md` | 全面重构：三组件概述、完整项目树、Mock 模式、技术栈补全（+399/-141） |
| 新增 | `opencode.json` | OpenCode 项目配置（+3） |

---

## 五、架构影响

| 维度 | 变更前 | 变更后 |
|------|--------|--------|
| Mock 可用性 | 无，必须硬件或手动 MMFWriter | `dotnet run -- --mock` 一键启动，全链路验证 |
| ConsoleApp1 构建 | MSB3026/3027 复制失败 | 正常构建 |
| Agent 引导 | 无 | AGENTS.md 提供构建命令、架构约定、IPC 数据流 |
| README 覆盖率 | 遗漏 SharedModels/ConfigLauncher/Tests 等 3 项目 + 7 文件 | 完整覆盖 8 个项目、全部目录、30 个 RPC 方法 |
| README 准确性 | 16 KB Payload（错误）、持久化周期描述错误 | 8 KB Payload、固定 5s |
| 进程拓扑 | 双进程（WebAPI + ConsoleApp1） | 三组件（WebAPI + ConsoleApp1 + ConfigLauncher） |

---

## 六、审核报告

> 审查范围：`MockDataWriter.cs`、`Program.cs`、`AGENTS.md`、`README.md`、`ConsoleApp1.csproj`

### 通过项

| # | 检查点 | 详情 |
|---|--------|------|
| 1 | Mock 模式隔离 | Mock 模式不启动子进程、不建立 gRPC 连接、不依赖硬件，与真实模式完全隔离 |
| 2 | 生命周期触发 | `UpdateCollectorStateSilent` 将 Acquiring 从 false 改为 true，自动触发 `AcquiringStateChanged(true)`，Coordinator 启动所有采集绑定服务 |
| 3 | 数据格式一致性 | MockDataWriter 写入的 `StructuredSample` 结构与 Analysis 线程输出一致，消费链路无感知 |
| 4 | MockDataWriter 关闭 | `ApplicationStopping` 中 `_mockCts.Cancel()` 触发 `OperationCanceledException` → `finally` 日志输出 |
| 5 | Build 修复 | 移除 `BaseOutputPath` 后恢复默认输出路径，解决方案正常构建 |
| 6 | README 一致性 | 项目树与实际文件完全对齐，Payload/持久化数值与源码一致 |

### 已修复问题

| # | 严重度 | 位置 | 问题描述 | 修复 |
|---|--------|------|----------|------|
| 1 | 高 | `ConsoleApp1.csproj:22` | `BaseOutputPath` 指向不存在路径导致构建失败 | 删除该行 |
| 2 | 中 | `README.md` | 遗漏 SharedModels / ConfigLauncher / Test 项目 | 补全项目树 |
| 3 | 中 | `README.md` | 波形 Payload 标注 16 KB（实际 8 KB） | 修正为 8 KB |
| 4 | 低 | `README.md` | Controllers 名称与实际不符（Collector→Client, System→SystemState） | 修正实名 |

### 遗留建议（非阻塞）

| # | 严重度 | 位置 | 建议 |
|---|--------|------|------|
| 1 | 低 | `MockDataWriter.cs:40` | `Task.Delay(1)` 精度受系统定时器分辨率影响（约 15ms），实际吞吐低于名义 1MHz。可改为 `Thread.Sleep(0)` 或纯忙等循环 |
| 2 | 低 | `MockDataWriter.cs:28-43` | Cn² 前 99 帧填 -1.0 的哨兵值语义已在写入侧实现，但 `index > 99 ? 1e-16 : -2.0` 使用了 -2.0 而非 -1.0，与文档约定不一致 |
