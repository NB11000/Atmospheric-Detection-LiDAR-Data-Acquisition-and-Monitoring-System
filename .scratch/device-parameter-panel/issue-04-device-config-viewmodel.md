# Issue 4: DeviceConfigViewModel

**Status:** `needs-triage`

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

[implementation-plan.md](./implementation-plan.md)

## What to build

实现 `DeviceConfigViewModel`，单 VM 管理全部四节配置表单。包含：子切段索引切换、进入时自动从 WebAPI 加载当前节配置、保存/刷新/恢复默认的 HTTP 编排、表单基本验证、状态消息。枚举字段存 int 值由 View 层 ComboBox 映射。使用 CommunityToolkit.Mvvm 的 `[ObservableProperty]` 和 `[RelayCommand]` 源生成器。

## Acceptance criteria

- [ ] `SelectedSubTabIndex` 绑定控制当前可见配置节（0=采集卡, 1=LiDAR, 2=激光器, 3=持久化）
- [ ] 切到新节时自动调用 `LoadConfig()` 从 WebAPI 拉取当前值
- [ ] `SaveCommand`：构建当前节的 DTO → 调用 `LauncherHttpClient.Update*()` → 返回成功则状态栏绿色"已保存"，失败则红色错误信息
- [ ] `RefreshCommand`：重新调用 `LoadConfig()` 覆盖当前表单值
- [ ] `ResetDefaultCommand`：调用 `GetDefault*()` 填充表单但不自动保存
- [ ] 采集卡枚举字段（SyncChannelIndex/ClockSourceIndex 等）存储为 int
- [ ] WebAPI 未连接时 `IsWebApiConnected = false`，所有按钮禁用
- [ ] HTTP 调用进行中 `IsBusy = true`，防止重复提交
- [ ] 切换子切段时取消进行中的 HTTP 调用
- [ ] 新增单元测试覆盖：加载成功/失败、保存成功/失败、刷新、恢复默认、子节切换
- [ ] 现有测试全部通过

## Blocked by

Issue 3 (LauncherHttpClient 配置 API 调用方法)
