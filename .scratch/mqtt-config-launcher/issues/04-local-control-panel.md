# Issue 04: 本地控制面板

- **Label**: needs-triage
- **Parent**: [implementation-plan.md](../implementation-plan.md)
- **Blocked by**: Issue 01, Issue 02, Issue 03

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

实现 `LauncherHttpClient` + `ControlViewModel` + `ControlPage`，以及完整的重启流程。

LauncherHttpClient 公共接口：

```
class LauncherHttpClient(baseUrl: string):
    Task<SystemStateDto> GetSystemState()
    Task<CommandResult> OpenDevice()
    Task<CommandResult> CloseDevice()
    Task<CommandResult> StartAcquisition()
    Task<CommandResult> StopAcquisition()
    Task<CommandResult> LaserConnect()
    Task<CommandResult> LaserDisconnect()
    Task<CommandResult> LaserOn()
    Task<CommandResult> LaserOff()
    Task<bool> ShutdownWebApi()
```

ControlPage 布局（三卡片）：

| 卡片 | 内容 |
|------|------|
| 采集卡控制 | 按钮：打开设备 / 关闭设备 / 开始采集 / 停止采集，每个按钮旁显示最近操作结果（成功/失败 + message） |
| 激光器控制 | 按钮：连接激光器 / 断开激光器 / 开激光 / 关激光 |
| 系统状态 | "刷新状态"按钮 + 只读状态面板（子进程连接、设备状态、采集状态、激光状态、时间戳） |

重启流程接入：
- 用户在 ConfigPage 修改配置并保存
- 弹框"配置已保存，WebAPI 需重启生效，是否立即重启？"
- 确认：`LauncherHttpClient.ShutdownWebApi()` → `WebApiProcessManager.StopAsync(baseUrl, 5s)` → `Start` → `WaitUntilReadyAsync(baseUrl, 30s)` → ControlPage
- 取消：仅保存，不重启

关闭启动器流程：
- 用户点击窗口关闭按钮
- 若 WebAPI 正在运行：弹确认框"关闭启动器不会停止 WebAPI，是否继续？"
- 确认后关闭启动器窗口

底部状态栏：BaseUrl 可编辑 + WebAPI 连接状态指示器（绿/红圆点 + 文字）

## Acceptance criteria

- [ ] 采集卡全部 4 个按钮可下发指令且显示结果
- [ ] 激光器全部 4 个按钮可下发指令且显示结果
- [ ] "刷新状态"按钮触发 GET api/system/state，面板更新
- [ ] 修改配置 → 确认重启 → 启动器关闭旧进程 → 拉起新进程 → 回到控制面板
- [ ] 修改配置 → 拒绝重启 → 仅保存，不重启
- [ ] 关闭启动器窗口 → 若 WebAPI 运行中 → 弹确认框 → 确认后关闭
- [ ] 底部状态栏：WebAPI 连接状态指示灯实时反映可达性

## Comments
