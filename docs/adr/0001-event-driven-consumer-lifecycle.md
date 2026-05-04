# 消费者生命周期由事件驱动，不由信号驱动

三个数据流水线消费者（WaveformPublishService、PersistenceService、LowFrequencyPublisher）的运行生命周期由 `AcquisitionLifecycleCoordinator` 集中驱动。Coordinator 订阅 `AcquiringStateChanged` 和 `MqttConnectionStateChanged` 两个状态事件，按各服务的 `RequiresMqttConnection` 属性判断启停条件：`CanRun = Acquiring && (!RequiresMqtt || MqttConnected)`。

**Considered Options:**

1. **信号驱动（EventWaitHandle）**——每次数据写入 MMF 时 Set 信号唤醒消费者。拒绝原因：消费者是抽样快照（7s ~ 5min 周期），非全量消费，1MHz 写入信号每秒 100 万次无意义唤醒；跨进程 EventWaitHandle 增加复杂度，三个服务各自需要节流逻辑，得不偿失。

2. **BackgroundService + ExecuteAsync 空实现**——WaveformPublishService 现有模式。拒绝原因：继承 BackgroundService 仅为了 DI 注册和 Dispose，ExecuteAsync 永远返回 CompletedTask，是反模式。

3. **每个服务直接订阅 SystemStateService 事件**——现状。拒绝原因：各服务各自判断启停条件，逻辑分散；MQTT 重连/断连时的恢复逻辑散布在 MqttRpcBackgroundService 和各个消费者中，难以统一管理。

**Consequences:**

- SystemStateService 新增 `MqttConnectionStateChanged` 事件，成为采集状态和 MQTT 连接状态的唯一真相源
- 三个服务全部注册为纯 Singleton，自行管理 CTS 和 IDisposable
- Coordinator 通过 `IEnumerable<IAcquisitionBoundService>` 发现所有服务，新增消费者只需注册接口
- MqttRpcBackgroundService 不再注入 WaveformPublishService，改为注入 SystemStateService 更新 MQTT 状态
