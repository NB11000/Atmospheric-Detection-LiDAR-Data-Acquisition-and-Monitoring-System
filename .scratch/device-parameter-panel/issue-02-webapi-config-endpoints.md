# Issue 2: WebAPI 新增 LidarAlgorithm 和 Persistence 配置 HTTP 端点

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

在 WebAPI 中新增 `LidarController`（route `api/lidar`）和 `PersistenceController`（route `api/persistence`），每个提供三个端点：`config/read`、`config/update`、`config/default`。同时为 `LaserController` 补齐 `config/default` 端点。在 `Program.cs` 补充 LidarAlgorithm 和 Persistence 配置的启动加载。写集成测试验证端点可达且行为正确。

## Acceptance criteria

- [ ] `POST api/lidar/config/read` 返回当前 `LidarAlgorithmConfig` JSON
- [ ] `POST api/lidar/config/update` 接收 JSON body，写入 DeviceConfig.json，返回更新后配置
- [ ] `POST api/lidar/config/default` 返回 `new LidarAlgorithmConfig()` 默认值
- [ ] `POST api/persistence/config/read` 返回当前 `PersistenceSettings` JSON
- [ ] `POST api/persistence/config/update` 接收 JSON body，写入 DeviceConfig.json，返回更新后配置
- [ ] `POST api/persistence/config/default` 返回 `new PersistenceSettings()` 默认值
- [ ] `POST api/laser/config/default` 返回 `new RadarConfig()` 默认值
- [ ] `Program.cs` 启动时加载 `LidarConfig` 和 `PersistenceConfig` 到全局静态属性
- [ ] 新增集成测试覆盖全部新增端点
- [ ] 现有测试全部通过

## Blocked by

Issue 1 (SharedModels 配置 DTO 迁移)
