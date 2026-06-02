# 03 — StopAsync 接入 PublishDeviceOfflineAsync + 兜底 + 竞态保护

- **Label**: needs-triage
- **Parent**: [IMPLEMENTATION_PLAN.md](../IMPLEMENTATION_PLAN.md)
- **Blocked by**: 01-will-payload-and-publisher

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

## What to build

改造 `MqttRpcBackgroundService.StopAsync` 三件事：

**1. 新增 `_shutdownCts`**
`private readonly CancellationTokenSource _shutdownCts = new()`，在 `StopAsync` 开头 Cancel。`ConnectAsync` 内使用 `CancellationTokenSource.CreateLinkedTokenSource(_shutdownCts.Token, callerToken).Token` 传参——确保 StopAsync 期间重连中的 `ConnectAsync` 被取消，不会 publish online 覆盖 offline。

**2. 正常 offline publish**
`DisconnectAsync` 之前调 `PublishDeviceOfflineAsync()`，try-catch 捕获异常，记 `success` 标记。

**3. 兜底逻辑**
- `success == true`：正常 `DisconnectAsync` + `Dispose`（Will 不触发）
- `success == false`：跳过 `DisconnectAsync` / `Dispose`，进程退出时 OS 关闭 TCP → Broker 检测非正常断连 → 触发 Will → retained offline

## Acceptance criteria

- [ ] `_shutdownCts` 在 `StopAsync` 中 Cancel，`ConnectAsync` 使用 linked CTS
- [ ] `PublishDeviceOfflineAsync` 在 `_shouldReconnect = false` 之前、`DisconnectAsync` 之前调用
- [ ] publish 成功 → `DisconnectAsync` 正常执行 → Will 不触发
- [ ] publish 失败 → 跳过 `DisconnectAsync` 和 `Dispose` → OS 关 TCP → Will 触发
- [ ] `UpdateMqttConnectionState(false)` 在 publish 之后调用（先发 offline 消息，再停服务）
- [ ] 重连竞态：`_shutdownCts.Cancel()` 后重连中的 `ConnectAsync` 被取消，不会 publish online
