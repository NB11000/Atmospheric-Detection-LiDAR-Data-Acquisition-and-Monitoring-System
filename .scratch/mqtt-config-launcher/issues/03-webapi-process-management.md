# Issue 03: WebAPI 进程管理

- **Label**: needs-triage
- **Parent**: [implementation-plan.md](../implementation-plan.md)
- **Blocked by**: Issue 01, Issue 02

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

实现 `WebApiProcessManager` 并接入 ConfigViewModel + 在 WebAPI 中新增 `api/system/shutdown` 端点。

### WebAPI 侧改动

新增 SystemController 端点：
```
POST api/system/shutdown → IHostApplicationLifetime.StopApplication() → 200 OK
```

### WebApiProcessManager

```
class WebApiProcessManager(webApiDirectory: string):
    Start() → Process
    StopAsync(baseUrl, timeoutSec) → Task<bool>
    WaitUntilReadyAsync(baseUrl, timeoutSec) → Task<bool>
```

关键行为：

1. **`Start()`**：`Process.Start("WebAPI.exe")`，工作目录设为 `webApiDirectory`
2. **`WaitUntilReadyAsync(baseUrl, timeoutSec)`**：GET 轮询 `{baseUrl}/` 直到返回 200 OK。轮询间隔 1s，总超时 timeoutSec（默认 30s）。成功返回 true，超时返回 false
3. **`StopAsync(baseUrl, timeoutSec)`** 流程：
   - HTTP POST `{baseUrl}/api/system/shutdown`（调新增的 shutdown 端点）
   - `Process.WaitForExit(timeoutMs)` 等待进程退出
   - 正常退出返回 true
   - 等待超时 → `Process.Kill()` → 返回 false
   - HTTP 不可达（可能已崩溃）→ 直接 Kill → 返回 false

### 接入 ConfigViewModel：
- "保存并启动"按钮绑定到完整流程：SaveConfig → MarkConfigured → Start → WaitUntilReady
- 就绪成功：MainWindow 切换到"控制"标签（将占位文本替换为 Issue 04 的控制面板）
- 就绪超时：弹错误框"WebAPI 启动超时，请检查 BaseUrl 是否正确"，用户可重试
- WebAPI 已运行时打开启动器（HTTP 可达）：跳过启动，直接切到控制标签

## Acceptance criteria

- [ ] WebAPI 新增 `POST api/system/shutdown` 端点，返回 200 OK 后进程退出
- [ ] 点击"保存并启动" → WebAPI 控制台窗口出现 → 启动器等待就绪 → 切换到控制标签
- [ ] WaitUntilReady 超时弹错误框，用户可手动重试
- [ ] "使用已有配置启动"按钮功能正常（跳过 SaveConfig，直接 Start）
- [ ] WebAPI 已运行时打开启动器：自动检测并直接进入控制标签
- [ ] StopAsync 调 shutdown 端点 → WebAPI 进程退出 → Start → WaitUntilReady 成功

## Comments
