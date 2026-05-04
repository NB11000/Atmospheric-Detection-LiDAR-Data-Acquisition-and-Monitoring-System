# `Program.coreBus` static → DI 重构

- **Label**: ready-for-human
- **Blocked by**: #2

## What to build

ConsoleApp1 引入 `Microsoft.Extensions.DependencyInjection`，将 `CoreDataBus` 从 `Program` 上的 `public static` 字段改为 DI 管理的单例。

- 创建 `HostBuilder` 风格的启动流程（`IHost` 或手动 `ServiceCollection`）
- `CoreDataBus` 注册为 singleton，Open() 在 DI 初始化后触发
- Analysis 通过构造函数或属性注入获取 `CoreDataBus`（而非 `Program.coreBus`）
- 消除 `Program` 上的 `public static CoreDataBus coreBus`

## Acceptance criteria

- [ ] Analysis 通过 DI 获取 CoreDataBus，不引用 `Program.coreBus`
- [ ] 现有功能（采集、分析、检测、UI）不退化
- [ ] 子进程启动时 DI 初始化在 gRPC 连接之前完成（时序不变）


