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


先用 /grill-me 问透需求；
让它整理成 PLAN.md；


帮我设计一个技能,用 /grill-me 或 
/grill-with-docs问透需求，让它自动整理计划

我发现从/grill-me 或 /grill-with-docs 到 /TDD 缺少一个自动串联的过程。帮我分析一下。 

做成一个自定义的skill技能，用于输出grill 阶段的结果供t d d使用。 


我的小宝是当g r i l l结束后，用户不需要调用它固定参数，而是 随着g r i l l结束后，自动调用。 或者说，g r i l l。 期间 就执行了也就是说我需要他跟g r i l l过程融合。 

并且当前有一个更重要的问题也就是运行 t d d时 发现并没有按照 测试驱动设计，这样的地点，它并没有先写测试而是直接实现了。 

根据你的建议。并且当前有一个更重要的问题也就是运行 t d d时 发现并没有按照 测试驱动设计，这样的地点，它并没有先写测试而是直接实现了。 

我认为，直接在  spec-from-grill中写明更好

我同意这个路径，但我希望他输出的这个设计方案或者设计文档。 可以让用户看到。 可以让开发者看到可以输出在项目中的某个地方 也就是说我希望这个技能能够同时，输出两种东西一种给agent看一种给开发者看。 

/grill-me /grill-with-docs的缺点是太简单：只提问，不会自动整理计划。

我的想法是先/grill-me /grill-with-docs 再to prd，然后再整理计划，再to-issue。你认为如何
