# PRD: 设备参数面板

**Status:** `needs-triage`

## Problem Statement

配置启动器缺少修改设备硬件参数和算法参数的入口。用户需要修改采集卡配置（采样率、通道、量程等）、激光雷达反演算法参数（接收孔径、K 常数等）、激光器硬件参数（功率、频率、串口等）和持久化数据目录时，只能手动编辑 `DeviceConfig.json` 文本文件，无 GUI 操作界面。

## Solution

在配置启动器内新增"设备参数"分页，通过 WebAPI HTTP 接口读写四组设备配置。每节独立操作（保存/刷新/恢复默认），仅在 WebAPI 已连接时可用。

## User Stories

1. 作为操作用户，我要在启动器中查看采集卡当前配置参数（设备编号、采样率、通道模式、时钟源、半满阈值、触发源、量程），以便确认硬件是否按预期配置。
2. 作为操作用户，我要修改采集卡的采样率并通过下拉框选择合法的通道模式、时钟源等枚举值，避免因手输非法值导致系统异常。
3. 作为操作用户，我要查看和修改激光雷达反演算法的物理参数（增益均衡系数、K 常数、接收孔径、路径长度、滑动窗口帧数、边界距离、激光波长、Angstrom 指数、暗电流采样数、采样率、盲区距离），以便根据环境条件调优反演精度。
4. 作为操作用户，我要查看和修改激光器的硬件参数（激光功率、调制频率、串口号、波特率），以便更换激光设备或调整工作状态。
5. 作为操作用户，我要查看和修改持久化数据目录路径，以便将 CSV 数据写入指定存储位置。
6. 作为操作用户，我要逐节独立保存配置，修改采集卡参数时不影响已调好的 LiDAR 算法参数。
7. 作为操作用户，修改变参数后如果发现不对，我要一键刷新重新加载 WebAPI 中已保存的当前值。
8. 作为操作用户，如果参数被改乱无法还原，我要一键恢复该节到出厂默认值。
9. 作为操作用户，保存参数后我要看到明确的成功/失败状态提示。
10. 作为操作用户，WebAPI 未启动时设备参数分页应不可用，避免给我一个看上去能用但实际无效的界面。

## Implementation Decisions

- 在 `SharedModels` 项目中集中放置 `CaptureCardConfig`、`RadarConfig`、`LidarAlgorithmConfig`、`PersistenceSettings` 四个 DTO，启动器和 WebAPI 共享类型定义。
- 配置读写通过 HTTP API：WebAPI 侧新增 LidarAlgorithm 和 Persistence 的 `config/read`、`config/update`、`config/default` 端点，并补齐 Radar 的 `config/default` 端点（采集卡已有全套端点）。
- 启动器通过扩展 `LauncherHttpClient` 新增 config read/update/getDefault 方法。
- 单个 `DeviceConfigViewModel` 管理四节表单状态，通过属性控制可见子切段。枚举字段使用中文 ComboBox 下拉选择，数值字段带单位 Label。WebAPI 未连接时 Tab 内容禁用不可见。
- 验证策略：前端做基本格式校验，业务正确性由 WebAPI 端点校验返回。
- 遵循项目中现有的 ViewLocator 模式和 CommunityToolkit.Mvvm 源生成器约定。

## Testing Decisions

- **DeviceConfigViewModel**：单元测试覆盖四节表单的加载/保存/刷新/恢复默认逻辑、HTTP 调用编排、错误状态、子切段切换。高价值。
- **LauncherHttpClient 扩展方法**：单元测试验证请求路径和响应反序列化，使用 mock `HttpMessageHandler`。
- **WebAPI 新增端点**：集成测试验证 endpoint 可达、config/update 写入文件正确、config/read 返回当前值、config/default 返回出厂默认值。
- **View**：不写自动化测试（Avalonia UI 测试成本高收益低），依赖手动验证。
- 参考现有测试项目 `AvaloniaApplication_ConfigLauncher.Tests` 和 `WebAPI.Tests` 的风格。

## Out of Scope

- 不持久化启动器侧的配置缓存——每次进入 Tab 都从 WebAPI 重新加载。
- 不支持批量保存全部四节的操作。
- 不支持配置导入/导出。
- 不通过 MQTT 通道读写配置（启动器不依赖 MQTT）。
- 不在 WebAPI 未运行时修改配置（不直接写 DeviceConfig.json）。
- 不对 LiDAR 算法的 11 个参数间的物理耦合关系做联动校验（超出启动器职责）。

## Further Notes

- CONTEXT.md 已更新，新增"设备参数面板"术语。
- 本 PRD 基于 grilled design session 得出，17 项设计决策均已确认。
