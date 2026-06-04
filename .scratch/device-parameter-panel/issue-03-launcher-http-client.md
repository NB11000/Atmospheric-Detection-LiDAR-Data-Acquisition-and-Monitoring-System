# Issue 3: LauncherHttpClient 配置 API 调用方法

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

在 `LauncherHttpClient` 中新增 12 个方法：4 组配置 × 3 操作（read / update / getDefault）。使用 `HttpClient` 调用 WebAPI 端点，JSON 序列化/反序列化，失败时返回 null 或适当的错误结果。编写单元测试使用 mock `HttpMessageHandler` 验证请求路径和响应处理。

## Acceptance criteria

- [ ] `GetCaptureCardConfig()` → GET `api/collector/command/config/read`
- [ ] `UpdateCaptureCardConfig(config)` → POST `api/collector/command/config/update` with JSON body
- [ ] `GetDefaultCaptureCardConfig()` → GET `api/collector/command/config/default`
- [ ] `GetRadarConfig()` / `UpdateRadarConfig()` / `GetDefaultRadarConfig()` → `api/laser/config/*`
- [ ] `GetLidarConfig()` / `UpdateLidarConfig()` / `GetDefaultLidarConfig()` → `api/lidar/config/*`
- [ ] `GetPersistenceConfig()` / `UpdatePersistenceConfig()` / `GetDefaultPersistenceConfig()` → `api/persistence/config/*`
- [ ] HTTP 超时/连接拒绝时返回 null（不抛异常）
- [ ] JSON 反序列化失败时返回 null
- [ ] 新增单元测试覆盖每组方法的成功和失败路径
- [ ] 现有测试全部通过

## Blocked by

Issue 1 (SharedModels 配置 DTO 迁移)
