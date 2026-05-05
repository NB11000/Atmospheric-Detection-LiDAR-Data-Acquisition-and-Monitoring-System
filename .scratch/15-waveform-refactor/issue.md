# WaveformPublishService 改造为 IAcquisitionBoundService

- **Category**: enhancement
- **State**: done
- **Blocked by**: #12, #13

## What to build

改造 `WaveformPublishService`，不再直接订阅 `SystemStateService.AcquiringStateChanged`，改为实现 `IAcquisitionBoundService`，由 Coordinator 统一管理启停。

- 实现 `IAcquisitionBoundService`（`RequiresMqttConnection = true`）
- 移除构造函数中的 `systemStateService.AcquiringStateChanged += OnAcquiringStateChanged`
- 移除 `OnAcquiringStateChanged` 回调方法
- 保留现有 `Start()` / `Stop()` / `_isRunning` 幂等逻辑
- 移除 `BackgroundService` 继承，改为纯 `IDisposable`
- `Program.cs` 注册改为 `AddSingleton<WaveformPublishService>()`（去掉 `AddHostedService`），同时注册为 `IAcquisitionBoundService`

## Acceptance criteria

- [ ] 编译通过
- [ ] Coordinator 可正常启停波形发布循环
- [ ] 原有波形发布功能不退化
- [ ] 不再依赖 `BackgroundService` 基类
