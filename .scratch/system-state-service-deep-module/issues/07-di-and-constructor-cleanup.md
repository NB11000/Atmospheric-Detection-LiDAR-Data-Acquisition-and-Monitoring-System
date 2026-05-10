---
Status: needs-triage
Parent: ../IMPLEMENTATION_PLAN.md
---

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

[实现计划](../IMPLEMENTATION_PLAN.md) — Slice 7

## What to build

完成 DI 注册和构造函数清理，将前 6 个 Slice 的变更接入 DI 容器。

**Program.cs DI 变更：**

1. SystemStateService 注册改为：
   ```csharp
   services.AddSingleton<SystemStateService>(sp => new SystemStateService(
       sp.GetRequiredService<ILogger<SystemStateService>>(),
       new Lazy<IMqttEventPublisher>(() => sp.GetRequiredService<IMqttEventPublisher>()),
       sp.GetRequiredService<ISignalRHubPublisher>()));
   ```
   或使用 `services.AddSingleton<SystemStateService>()` + .NET DI 自动解析 Lazy<T>（需验证 .NET 8 是否原生支持 Lazy<T> 的 DI 解析）。若原生不支持，使用上面的工厂委托方式。

2. IMqttEventPublisher / ISignalRHubPublisher 接口注册：
   ```csharp
   services.AddSingleton<IMqttEventPublisher>(sp => sp.GetRequiredService<MqttEventPublisher>());
   services.AddSingleton<ISignalRHubPublisher>(sp => sp.GetRequiredService<SignalRHubPublisher>());
   ```
   （将接口指向现有 Singleton 实现）

**构造函数清理：**

1. GrpcServiceImpl：移除 `SignalRHubPublisher hubPublisher` 和 `MqttEventPublisher mqttEventPublisher` 两个构造函数参数及对应的 `_hubPublisher`、`_mqttEventPublisher` 字段
2. CniLaser：移除 `SignalRHubPublisher signalRHubPublisher` 和 `MqttEventPublisher mqttEventPublisher` 两个构造函数参数及对应的 `_hubPublisher`、`_mqttEventPublisher` 字段。保留 `IServiceProvider`（用于获取 SystemStateService）

**测试（TDD）：**
不新增测试——DI 容器正确性由运行时启动验证。此 Slice 完成后启动 WebAPI，验证：
- 无 DI 解析异常
- MqttRpcBackgroundService 正常启动
- SystemStateService 被正确注入 GrpcServiceImpl / CniLaser / MqttRpcBackgroundService

## Acceptance criteria

- [ ] Program.cs 中 SystemStateService 注入 Lazy<IMqttEventPublisher> + ISignalRHubPublisher
- [ ] Program.cs 中 IMqttEventPublisher → MqttEventPublisher、ISignalRHubPublisher → SignalRHubPublisher 接口映射
- [ ] GrpcServiceImpl 构造函数不再有 MqttEventPublisher / SignalRHubPublisher 参数
- [ ] CniLaser 构造函数不再有 MqttEventPublisher / SignalRHubPublisher 参数
- [ ] WebAPI 启动无 DI 解析异常
- [ ] `_mqttEventPublisher` / `_hubPublisher` 字段从 GrpcServiceImpl / CniLaser 中完全移除完毕

## Blocked by

- [05-grpc-service-split](05-grpc-service-split.md)
- [06-cnilaser-cleanup](06-cnilaser-cleanup.md)
