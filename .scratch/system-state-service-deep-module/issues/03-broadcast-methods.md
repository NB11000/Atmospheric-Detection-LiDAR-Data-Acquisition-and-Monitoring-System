---
Status: needs-triage
Parent: ../IMPLEMENTATION_PLAN.md
---

## Execution Rules

> **此 Issue 执行顺序不可变更，必须遵循 TDD 红绿重构循环：**
>
> **1. RED** — 先写一个测试，确认测试 FAIL。禁止一次写多个测试。
> **2. GREEN** — 写最少代码让当前测试 PASS。禁止预判未来测试。
> **3. REFACTOR** — 消除重复、深化模块。禁止 RED 期间重构。
>
> **硬禁止：**
> - 禁止"先全部实现再补测试"（水平切片反模式）
> - 禁止跳过 RED 直接写 GREEN
> - 测试必须通过公共接口验证行为，不耦合实现细节
> - 每次循环只一个测试 → 一个实现，垂直切片推进

## Parent

[实现计划](../IMPLEMENTATION_PLAN.md) — Slice 3

## What to build

在 SystemStateService 中新增路径 [B] 的 Broadcast 方法，实现"更新缓存 + 双通道广播"。

**新增方法：**
- `UpdateCollectorStateAndBroadcast(Func<CollectorStateDto, CollectorStateDto> updater, string eventType, string reason)` — 更新缓存 + `_ = _mqttEventPublisher.Value.PublishStateChangedAsync(eventType, "collector", reason, state.LastMessage)` + `_ = _signalRHubPublisher.PublishStateChangedAsync(eventType, "collector", reason, state.LastMessage)`
- `UpdateLaserStateAndBroadcast(Func<LaserStateDto, LaserStateDto> updater, string eventType, string reason)` — 同上，source="laser"
- `ResetCollectorStateAndBroadcast(string reason)` — 重置缓存到默认值 + 双通道推送 collector_disconnected

**关键实现细节：**
- 推送使用 `_ =` fire-and-forget，不阻塞状态更新
- 每个推送调用内部已有 try/catch（Publisher 内部），推送失败不污染状态
- Acquiring 变化时仍触发 AcquiringStateChanged 内部事件（与 Silent 一致）
- `_mqttEventPublisher` 通过 `Lazy<IMqttEventPublisher>` 延迟访问，避免构造循环

**测试（TDD）：**
1. `UpdateCollectorStateAndBroadcast_UpdatesCache` — 状态缓存正确更新
2. `UpdateCollectorStateAndBroadcast_PublishesToBothChannels` — Mock 验证两 Publisher 各被调用 1 次，eventType/source/reason 参数正确
3. `UpdateCollectorStateAndBroadcast_AcquiringChanged_FiresInternalEvent` — Acquiring 变化触发内部事件
4. `UpdateLaserStateAndBroadcast_PublishesBothChannels` — 激光器双通道广播
5. `ResetCollectorStateAndBroadcast_ResetsAndPublishes` — 缓存重置 + 推送
6. `Broadcast_PublishFailure_DoesNotCorruptState` — 推送抛异常时状态更新正常完成（Mock 设置 throw）

## Acceptance criteria

- [ ] `UpdateCollectorStateAndBroadcast` 更新缓存 + MQTT + SignalR 均被调用
- [ ] `UpdateLaserStateAndBroadcast` 更新缓存 + MQTT + SignalR 均被调用
- [ ] `ResetCollectorStateAndBroadcast` 重置缓存 + 双通道推送 collector_disconnected
- [ ] 推送均为 fire-and-forget（`_ =`），不阻塞状态更新
- [ ] 推送失败不污染状态更新
- [ ] Acquiring 变化时 AcquiringStateChanged 触发
- [ ] 6 个单元测试全部通过

## Blocked by

- [02-silent-methods](02-silent-methods.md)
