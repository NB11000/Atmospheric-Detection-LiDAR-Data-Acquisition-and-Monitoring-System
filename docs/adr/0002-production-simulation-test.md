# 模拟生产环境测试架构

为检测生产环境 bug，构建三层测试体系（S/M/L 阶段），由独立 SimulationRunner 进程编排主控和子进程，通过三方观测通道（REST API + MQTT 订阅 + 子进程健康端点）自动断言。

**关于为什么选择 `--mock` 标志而非硬件抽象接口**：`AD_Controlcs` 的 ADWork 线程直接调用 USB1602 SDK，无抽象层。抽取 `IAdHardware` 接口需要改生产代码路径且有风险；`--mock` 标志仅影响启动路径（跳过硬件初始化、启用 MockDataGenerator 线程替换 ADWork），对生产路径零影响。代价是 `AD_Controlcs` 内部出现一条 mock 分支，但该分支受 `ADDataTest_RunFlag` 统一控制，生命周期对称。

**关于子进程健康端点选择 HttpListener 而非 ASP.NET Core**：子进程目前是纯控制台应用，无任何 Web 依赖。HttpListener 是 .NET 内置，零 NuGet 包引入，在 `--mock` 下才启动。ASP.NET Core 需要 `Microsoft.AspNetCore.App` 框架引用，增加 10+ 间接依赖，对子进程纯采集角色是过度引入。

**关于健康端口固定分配而非自动发现**：`--mock` 下子进程健康端口固定 19999，主控 gRPC 端口不固定但 Runner 只通过 REST（5135）间接验证 gRPC 状态，无需直接连接。固定端口避免了文件 IO 或 stdout 解析的脆弱性。

**关于 S/M 阶段连真实 MQTT Broker**：测试数据通过独立 MachineId `daq-test-01` 隔离，不污染线上 topic。L 阶段如需断连重连测试，再评估是否引入本地 Docker EMQX。

## 考虑过的选项

- **子进程健康通道走 gRPC 复用**：主控转发健康数据给 Runner。被拒——主控挂了就无法观测子进程，违背故障隔离原则。
- **本地 Docker MQTT 替代真实 Broker**：隔离最干净，但增加 Docker 环境依赖。S/M 阶段先用真实 Broker，L 阶段按需评估。
- **SimulationRunner 通过 MMF 直接读 CoreDataBus 验证数据完整性**：会引入物理磁盘/共享内存依赖，SimulationRunner 必须和被测进程在同一台机器上。被拒——现有 REST + MQTT + 健康端点已覆盖所需断言。
