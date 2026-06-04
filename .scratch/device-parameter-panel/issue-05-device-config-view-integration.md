# Issue 5: DeviceConfigView + MainWindow 集成

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

编写 `DeviceConfigView.axaml` 渲染设备参数面板的完整 UI，包含顶部水平子 Tab 切换四节配置、每节表单使用 Grid 布局、三按钮操作栏、状态栏。在 `MainWindow.axaml` 新增第 4 个 Tab "设备参数"。`MainWindowViewModel` 中注册 `DeviceConfigVM` 实例并注入 `LauncherHttpClient` 和 WebAPI 连接状态。手动验证完整交互流程。

## Acceptance criteria

- [ ] `DeviceConfigView.axaml` 包含顶部水平 Tab 切换四个配置节
- [ ] 采集卡表单：DeviceId(数字框) + 6 个 ComboBox + SampleRate(数字框)，每行 Label 带中文说明
- [ ] LiDAR 表单：11 个数值输入框，Label 带单位标注（如"接收孔径 (m):"）
- [ ] 激光器表单：LaserPower(数字框) + LaserModulationFrequency(数字框) + SerialPort(文本框) + BaudRate(数字框)
- [ ] 持久化表单：DataDirectory(文本框)
- [ ] 每节底部三个按钮"保存"/"刷新"/"恢复默认"横向排列，右侧状态栏
- [ ] MainWindow TabControl 新增 `TabItem Header="设备参数"`
- [ ] WebAPI 未连接时第 4 个 Tab 内容不可见
- [ ] WebAPI 连接后切到该 Tab 自动加载当前节配置
- [ ] 手动验证：修改采集卡采样率 → 保存 → 看到绿色"已保存" → C 文件确认 DeviceConfig.json 已更新
- [ ] 手动验证：修改 LiDAR 参数 → 恢复默认 → 表单回到出厂值
- [ ] 手动验证：子切段切换不丢失其他节的修改状态
- [ ] 编译通过

## Blocked by

Issue 4 (DeviceConfigViewModel)
