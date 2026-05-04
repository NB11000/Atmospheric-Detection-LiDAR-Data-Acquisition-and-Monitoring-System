# 消费者生命周期事件驱动化

- **Label**: done

## Problem Statement

数据流水线的三个消费者（波形发布、低频发布、持久化）各自独立于采集生产者的生命周期运行。波形发布已在 `WaveformPublishService` 中通过 `AcquiringStateChanged` 事件驱动启停，但低频发布和持久化仍设计为自驱定时器后台服务——它们不感知采集状态，即使没有数据写入也持续轮询 `CoreDataBus.TryReadLatestSingle()`。反过来，采集停止后消费者无法自动退出，导致孤立消费。MQTT 断连/重连时，波形发布的手动 CTS 管理逻辑散布在 `MqttRpcBackgroundService` 中，低频发布和持久化完全不受其影响。整体缺陷：生产者与消费者的生命周期没有系统性地关联。

## Solution

引入 **采集生命周期协调器** 统一管理所有生命周期绑定的消费者。每个消费者实现 `IAcquisitionBoundService` 接口，声明是否依赖 MQTT 连接。协调器订阅 `SystemStateService` 的两个事件——`AcquiringStateChanged` 和（新增的）`MqttConnectionStateChanged`——按统一公式 `CanRun = Acquiring && (!RequiresMqtt || MqttConnected)` 分发启停。消费者不再直接订阅事件，也不继承 `BackgroundService`，改为纯 `Singleton` + 内部 CTS 管理。

## User Stories

1. 作为系统运维人员，我希望采集停止后所有数据消费者自动停止，避免无数据时持续轮询浪费 CPU 和日志噪音
2. 作为系统运维人员，我希望 MQTT Broker 断连时波形发布和低频发布自动停止，MQTT 恢复后自动恢复，无需手动干预
3. 作为系统运维人员，我希望 MQTT 断连时持久化服务继续运行，不因 Broker 波动影响本地数据落盘
4. 作为开发者，我希望新增一个数据消费者时只需实现一个接口并注册到 DI 容器，无需修改启停逻辑或协调器代码
5. 作为开发者，我希望每个消费者的 Start/Stop 是线程安全且幂等的，即使协调器在并发事件中重复调用也不会出错
6. 作为开发者，我希望消费者之间的故障是隔离的——一个消费者的异常不会导致其他消费者停止
7. 作为系统架构师，我希望 MQTT 连接状态和采集状态在 `SystemStateService` 中统一管理，成为系统状态的唯一真相源
8. 作为前端用户，我希望采集开始后低频数据（7s 周期）和高频波形（毫秒级）自动出现在 MQTT 主题上，采集停止后自动消失
9. 作为数据分析人员，我希望持久化 CSV 按小时分片，采集期间自动写入，采集停止后不再产生空文件

## Implementation Decisions

1. **事件驱动，不信号驱动**：消费者是抽样快照（7s ~ 5min 周期），非全量消费，跨进程 `EventWaitHandle` 每秒 100 万次无意义唤醒，得不偿失。选择 `SystemStateService` 的状态变化事件驱动。

2. **纯 Singleton，不继承 BackgroundService**：`BackgroundService.ExecuteAsync` 空实现是反模式。消费者注册为 `Singleton`，由协调器调用 `Start()`/`Stop()`，各自管理 `CancellationTokenSource` 和 `IDisposable`。

3. **统一协调器，不分散订阅**：协调器订阅两个事件，集中判断。避免每个消费者各自判断启停条件导致逻辑分散，也避免 MQTT 重连恢复逻辑散布在多个类中。

4. **接口属性声明前置条件**：每个服务通过 `RequiresMqttConnection` 属性声明自己的运行条件，协调器通过 `IEnumerable<IAcquisitionBoundService>` 发现所有服务，新增消费者无需改协调器。

5. **协调器不加锁，服务各自幂等**：每个服务用 `lock` + `_isRunning` 双重检查保证 `Start()`/`Stop()` 幂等，协调器只需遍历调用，无需追踪服务状态。

6. **SystemStateService 作为唯一状态源**：新增 `MqttConnectionStateChanged` 事件和 `UpdateMqttConnectionState()` 方法，与 `AcquiringStateChanged` 对称。`MqttRpcBackgroundService` 在断连/重连时更新 MQTT 状态，不再直接操控 `WaveformPublishService`。

7. **CoreDataBus 公开时间校准属性**：`ReferenceTick`、`ReferenceUtcTicks` 等六个属性由指针字段改为公开只读属性，供 `PersistenceService` 和 `LowFrequencyPublisher` 调用 `TimeHelper.ToUtcDateTime()` 还原 UTC 时间。

## Implementation Modules

| 模块 | 类型 | 职责 |
|------|------|------|
| `IAcquisitionBoundService` | 接口 | `Start()` / `Stop()` / `RequiresMqttConnection` |
| `AcquisitionLifecycleCoordinator` | Singleton | 订阅两事件，遍历服务按 `CanRun` 公式分发启停 |
| `SystemStateService`（修改） | Singleton | 新增 `MqttConnectionStateChanged` 事件 + `UpdateMqttConnectionState()` |
| `WaveformPublishService`（改造） | Singleton | 移除事件订阅和 `BackgroundService` 继承，实现 `IAcquisitionBoundService` |
| `MqttRpcBackgroundService`（改造） | HostedService | 注入 `SystemStateService` 替代 `WaveformPublishService`，三处改为 `UpdateMqttConnectionState()` |
| `PersistenceService` | Singleton | 30s 周期，`RequiresMqttConnection=false`，CSV 按小时分片 |
| `LowFrequencyPublisher` | Singleton | 7s 周期，`RequiresMqttConnection=true`，JSON → `daq/{id}/lowfreq` |
| `CoreDataBus`（修改） | Singleton | 暴露 `ReferenceTick` 等 6 个公共只读属性 |

## Testing Decisions

- 测试关注外部行为，不测实现细节。Coordinator 测试通过 `SpyService`（记录 Start/Stop 调用次数）验证启停分发逻辑。
- `SystemStateService` 测试直接订阅事件验证值变/不变时的触发行为。
- 服务幂等测试通过一个复刻 `lock` + `_isRunning` 模式的 `TestService` 验证模式正确性。
- 测试文件：`AcquisitionLifecycleCoordinatorTests`(6 个)、`SystemStateServiceTests`(2 个)、`AcquisitionBoundServiceTests`(3 个)，总计 11 个新测试，与已有 21 个测试合计 32 个全部通过。

## Out of Scope

- 信号驱动（EventWaitHandle / MMF 信号位）——已评估并拒绝，见 ADR 0001
- 子进程侧检测线程的生命周期管理——检测线程运行在子进程内，通过 `DetectionChannel` 通信，与主控进程消费者生命周期属不同问题域
- 持久化周期和低频发布周期的运行时动态调整——当前为编译时常量（30s / 7s），未来可通过 `IOptions` 或 `appsettings.json` 配置

## Further Notes

- ADR 记录：[docs/adr/0001-event-driven-consumer-lifecycle.md](../../docs/adr/0001-event-driven-consumer-lifecycle.md)
- 领域术语：CONTEXT.md 新增「采集绑定服务」「采集生命周期协调器」两个术语，Key Decision #11 #12
- 依赖链：#12 + #14 → #13 → #06, #07, #15, #16。全部 7 个 issue 已实现并标记 done
