# 状态机优化方案：WebAPI 本地维护采集卡状态

> 文档版本：v1.0  
> 创建日期：2026-04-18  
> 状态：方案确认，待实施

---

## 一、问题背景

当前系统架构中，`SystemStateService` 每次生成系统状态快照（`SystemStateDto`）时，都需要通过 gRPC 双向流向数据采集子进程发送 `GET_COLLECTOR_STATE` 命令，等待子进程序列化 `AD_Controlcs.GetRuntimeState()` 并返回 JSON，再反序列化为 `CollectorStateDto`。

该流程涉及一次完整的 gRPC 请求-响应关联周期（生成 GUID → 注册 TaskCompletionSource → 发送命令 → 等待响应，超时 10 秒），导致：

- 每次快照生成都产生一次跨进程 IPC 往返
- 子进程每次被查询都需要执行 JSON 序列化
- 快照接口（`GET /api/system/state`）响应延迟受 IPC 开销影响
- SignalR 状态推送（`PublishStateChangedAsync`）内部也调用了 `GetSystemStateAsync()`，进一步放大 IPC 频次

---

## 二、优化目标

让 WebAPI 在本地维护一份采集卡状态缓存（`CollectorStateDto`），**基于已有的 3 种 gRPC 消息类型推断并更新采集卡状态**，使快照生成路径不再包含任何跨进程通信。

---

## 三、方案设计

### 3.1 核心思路

WebAPI 通过以下 3 种已有信息通道推断采集卡状态，维护本地缓存：

| 信息来源 | 消息类型 | 状态推断方式 |
|---|---|---|
| WebAPI 发送命令后收到的响应 | `command_response`（消息类型 3） | 解析响应的 `Content`、`MHandle`、`ErrorCode`，根据原始命令推断操作结果 |
| 子进程主动上报的信息 | `data_report`（消息类型 1） | 识别关键消息内容，更新状态或 `LastMessage` |
| 子进程主动上报的错误 | `Error`（消息类型 2） | 根据 `ErrorCode` 判断错误类型，更新对应状态字段 |

### 3.2 架构图

```
                  ┌─────────────────────────────────────┐
                  │      SystemStateService (WebAPI)     │
                  │                                     │
                  │  _cachedCollectorState ◄────────┐   │
                  │       ▲       ▲       ▲         │   │
                  │       │       │       │         │   │
                  └───────┼───────┼───────┼─────────┼───┘
                          │       │       │         │
              ┌───────────┘       │       └──────┐  │
              │                   │              │  │
    ┌─────────┴─────┐  ┌─────────┴──────┐  ┌────┴──┴──────────┐
    │ command_      │  │ data_report    │  │ Error            │
    │ response      │  │ (消息类型1)    │  │ (消息类型2)       │
    │ (消息类型3)   │  │                │  │                  │
    │               │  │ 子进程主动上报  │  │ 子进程主动上报    │
    │ 命令响应推断   │  │ 状态/心跳信息  │  │ 错误+ErrorCode   │
    │ OPEN→已打开   │  │                │  │ 硬件异常→状态回退 │
    │ START→采集中  │  │                │  │                  │
    │ STOP→已停止   │  │                │  │                  │
    └───────────────┘  └────────────────┘  └──────────────────┘
              ▲                 ▲                    ▲
              │                 │                    │
    ┌─────────┴─────────────────┴────────────────────┴────┐
    │              GrpcServiceImpl.Communicate()           │
    │              (统一消息接收与状态更新入口)              │
    └─────────────────────────────────────────────────────┘
              ▲
              │ gRPC 双向流
    ┌─────────┴───────────────────────────┐
    │    ConsoleApp1 (数据采集子进程)       │
    │    GrpcClient.HandleServerCommand()  │
    │    GrpcClient.SendMessage()          │
    │    GrpcClient.SendErrorMessage()     │
    └─────────────────────────────────────┘
```

### 3.3 消息类型 3：`command_response` 状态推断规则

这是最核心、最可靠的状态推断来源。子进程 `HandleServerCommand()` 对每个命令都有明确的响应格式：

| 命令 | 响应 `Content` | 响应 `MHandle` | 推断的状态更新 |
|---|---|---|---|
| `OPEN_DEVICE` | `Device_Opened()` 返回值 | 设备句柄值 | `MHandle > 0` → `DeviceOpened=true, Handle=MHandle`；否则 → 打开失败，仅更新 `LastMessage` |
| `OPEN_DEVICE_AGAIN` | `Device_Opened_again()` 返回值 | 设备句柄值 | 同 `OPEN_DEVICE` |
| `CLOSE_DEVICE` | `"采集卡设备已关闭"` | — | `DeviceOpened=false, Handle=0, Acquiring=false` |
| `START_AD` | `"AD_STARTED"` 或 `"采集卡未打开，无法开始采集"` | — | `Content == "AD_STARTED"` → `Acquiring=true`；否则 → 不变 |
| `STOP_AD` | `"AD_STOPPED"` | — | `Acquiring=false` |
| `EXIT` | `"EXIT_OK"` | — | 全部状态重置为默认值 |
| 任意命令异常 | `ErrorCode == "COMMAND_HANDLE_FAILED"` | — | 状态不变（操作未成功执行），仅更新 `LastMessage` |

**关键设计**：需要在 WebAPI 侧维护 `requestId → command` 的映射表（`ConcurrentDictionary<string, string>`），在 `SendCommandToClientAndWaitResponse()` 注册等待器时同步写入映射，在 `command_response` 到达时查找原始命令名称。

### 3.4 消息类型 1：`data_report` 状态推断规则

当前 `data_report` 在服务端仅做日志记录。优化后需增加内容模式识别：

| 上报内容 | 推断的状态更新 |
|---|---|
| `"子进程准备完毕"` | 冗余确认 `ProcessConnected=true`（主要靠 `_clientStreams` 判断） |
| 其他普通消息 | 更新 `LastMessage` |
| 结构化状态消息（扩展规划） | 需约定格式前缀或使用 `AdResponse.Data` 字段 |

**扩展规划**：建议在 `data_report` 中引入子类型约定。利用 `Content` 的前缀协议区分消息类别：

- `[STATE]{...}` — 结构化状态通知，WebAPI 解析 JSON 更新缓存
- `[LOG]...` — 纯文本日志，仅更新 `LastMessage`
- 无前缀 — 向后兼容，当作普通日志处理

### 3.5 消息类型 2：`Error` 状态推断规则

当前 `Error` 消息的 `ErrorCode` 始终为 `"NONE"`，无法区分错误类型。优化后需约定错误码规范：

| `ErrorCode` 值 | 含义 | 推断的状态更新 |
|---|---|---|
| `DEVICE_DISCONNECTED` | 设备意外断开 | `DeviceOpened=false, Handle=0, Acquiring=false` |
| `ACQUISITION_FAILED` | 采集过程异常终止 | `Acquiring=false` |
| `DEVICE_OPEN_FAILED` | 设备打开失败 | `DeviceOpened=false` |
| `GENERAL_ERROR` | 一般性错误（不影响硬件状态） | 仅更新 `LastMessage` |
| `NONE`（向后兼容） | 未分类错误 | 仅更新 `LastMessage` |

### 3.6 连接/断开生命周期管理

| 生命周期事件 | 触发位置 | 状态更新 |
|---|---|---|
| 子进程连接 | `Communicate()` 首次接收消息，`_clientStreams.TryAdd` | 初始化缓存：`ProcessConnected=true`，其余字段保持默认值 |
| 子进程正常断开 | `Communicate()` 的 `finally` 块 | 重置缓存：`ProcessConnected=false, DeviceOpened=false, Acquiring=false, Handle=0` |
| 子进程崩溃 | 同断开（gRPC 流异常中断触发 `finally`） | 同断开处理 |

### 3.7 改造后的快照生成流程

```
GetSystemStateAsync() — 改造后
  ├─ 1. 直接读取 _cachedCollectorState（本地内存，零开销）
  ├─ 2. 读取 CniLaser 单例（进程内，零开销）
  ├─ 3. BuildUiHints() 纯函数计算
  └─ 4. 组装 SystemStateDto
       结果：0 次 IPC
```

---

## 四、改造前后对比

| 维度 | 改造前（Pull 模式） | 改造后（推断 + 缓存） |
|---|---|---|
| 快照生成 IPC 次数 | 每次 1 次 gRPC 往返 | 0 次 |
| 快照生成耗时 | 取决于 IPC 延迟（毫秒级） | 纯内存操作（微秒级） |
| 子进程 CPU 开销 | 每次查询触发 JSON 序列化 | 仅命令响应时序列化（频率极低） |
| 状态一致性 | 强一致（每次查询实时获取） | 最终一致（基于命令响应 + 主动上报） |
| .proto 文件修改 | — | 无需修改 |
| `GetSystemStateAsync()` | 异步（await gRPC） | 可退化为同步方法 |

---

## 五、风险与应对

### 风险 1：状态与硬件实际不一致

**场景**：WebAPI 发送 `OPEN_DEVICE` 推断 `DeviceOpened=true`，但子进程执行过程中崩溃。

**应对**：
- 子进程断开时（`finally` 块），一律重置缓存为默认值——这是最终兜底
- 保留 `GET_COLLECTOR_STATE` 命令不删除，可用于手动诊断或可选的低频健康检查（如每 60 秒一次，不在快照热路径中）

### 风险 2：硬件异常导致的状态变更无法感知

**场景**：采集过程中 USB 设备意外断开，子进程未收到任何 WebAPI 命令，`command_response` 不会触发。

**应对**：
- 依赖消息类型 2（`Error`），子进程的 `ADWork` 线程在检测到 USB 读取失败时调用 `SendErrorMessage()` 并携带 `DEVICE_DISCONNECTED` 错误码
- WebAPI 收到该错误码后更新缓存状态
- 需在子进程关键硬件操作的 `catch` 块中统一加入错误上报

### 风险 3：`data_report` 消息类型区分粒度不足

**场景**：`data_report` 混合了心跳、日志、状态通知，难以可靠提取状态信息。

**应对**：
- 引入 `Content` 前缀协议（如 `[STATE]`、`[LOG]`）
- 或利用 `AdResponse.Data`（`google.protobuf.Any` 类型，当前未使用）携带结构化数据
- 保持向后兼容：无前缀的消息默认当作日志处理

### 风险 4：缓存并发读写

**场景**：`Communicate()` 中更新缓存与 `GetSystemStateAsync()` 中读取缓存可能并发执行。

**应对**：
- 使用不可变对象替换模式：每次更新时创建新的 `CollectorStateDto` 实例，通过 `Interlocked.Exchange` 原子替换引用
- 读取时直接读引用，无需加锁

---

## 六、涉及的代码文件

| 文件 | 改动范围 |
|---|---|
| `WebAPI/Service/SystemStateService.cs` | 新增 `_cachedCollectorState` 缓存字段；`GetCollectorStateAsync()` 改为读缓存；新增 `UpdateCollectorState()` 方法 |
| `WebAPI/Service/GrpcServiceImpl.cs` | `Communicate()` 的 3 个消息类型分支增加状态更新调用；`SendCommandToClientAndWaitResponse()` 增加 `requestId → command` 映射 |
| `ConsoleApp1/Service/GrpcClient.cs` | `SendErrorMessage()` 填充有意义的 `ErrorCode`；关键硬件异常点增加 `SendErrorMessage` 调用 |
| `WebAPI/Protos/Grpc.proto` | 无需修改 |

---

## 七、保留的兜底能力

`GET_COLLECTOR_STATE` 命令**不删除**，保留以下用途：

1. **手动诊断 API**：提供独立接口供运维人员主动查询子进程真实状态，与缓存对比
2. **可选健康检查**：以极低频率（如 60 秒）定时校验缓存与子进程实际状态的一致性，发现偏差时以子进程为准修正缓存并记录告警日志
3. **开发调试**：开发阶段验证状态推断逻辑的正确性
