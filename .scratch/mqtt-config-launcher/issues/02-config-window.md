# Issue 02: 配置窗口（首次配置 + 已有配置摘要）

- **Label**: needs-triage
- **Parent**: [implementation-plan.md](../implementation-plan.md)
- **Blocked by**: Issue 01

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

构建 MainWindow + ConfigPage + ConfigViewModel + ControlPage（占位），实现配置窗口的两种模式。

**TabControl 三标签：** 配置 / 控制（占位）/ 日志（占位）

**模式 A — 首次配置（`.mqtt_configured` 不存在）：**
- 展示配置表单，6 个核心字段：BrokerHost / BrokerPort / MachineId / Username / Password / UseTls（复选框）
- MachineId 预填 `Environment.MachineName`，BrokerPort 默认 8883，UseTls 默认勾选
- 5 个高级字段折叠在"高级设置"折叠面板内（默认隐藏）：AllowUntrustedCertificates / CaCertificatePath（含"选择文件"按钮，过滤 .crt/.pem/.cer） / RpcTimeoutSeconds / ReconnectDelaySeconds / WaveformPublishIntervalMs
- 表单验证：BrokerHost 为空时"保存并启动"按钮禁用，Username 为空时弹警告但允许保存
- "保存并启动"按钮（功能在 Issue 03 接入）
- "退出"按钮

**模式 B — 已有配置（`.mqtt_configured` 存在 + WebAPI 未运行）：**
- 展示配置摘要（只读）：Broker:Port / MachineId / Username（掩码显示，仅首尾字符）/ TLS 状态 / BaseUrl
- 三个按钮："使用已有配置启动"（Issue 03 接入）、"修改配置"（切换到模式 A 表单，预填当前值）、"退出"

**底部状态栏：** BaseUrl 可编辑输入框（LoadBaseUrl 初始化 + 失焦自动 SaveBaseUrl）+ WebAPI 连接状态指示器（Issue 03 激活）

**控制标签页（占位）：** 显示"WebAPI 未启动"文本，Issue 03/04 替换

## Acceptance criteria

- [ ] 首次运行：展示配置表单，MachineId 预填主机名，高级字段折叠隐藏
- [ ] 表单验证：BrokerHost 为空时"保存并启动"按钮不可用
- [ ] 已有配置：展示摘要（Broker:Port / MachineId / 掩码用户名 / TLS / BaseUrl）+ 三按钮
- [ ] "修改配置"按钮：切换到表单，字段预填当前值
- [ ] CA 证书文件选择器：打开文件对话框，过滤证书后缀
- [ ] 保存按钮：调用 ConfigManager.SaveConfig + MarkConfigured（本 Issue 只验证文件落盘，不启动进程）
- [ ] BaseUrl 输入框存在，默认值从 ConfigManager.LoadBaseUrl() 加载，修改失焦后 SaveBaseUrl 持久化
- [ ] 退出按钮：关闭应用
- [ ] TabControl 三标签存在，控制标签显示"WebAPI 未启动"占位文本
- [ ] Username 为空时保存弹警告"用户名未填写，EMQX Serverless 需要认证凭据"，允许继续

## Comments
