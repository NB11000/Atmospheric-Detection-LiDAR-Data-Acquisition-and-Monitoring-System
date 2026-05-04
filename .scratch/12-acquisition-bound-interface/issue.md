# IAcquisitionBoundService 接口定义

- **Label**: done
- **Blocked by**: —

## What to build

定义 `IAcquisitionBoundService` 接口，作为所有生命周期绑定采集状态的消费者的统一契约。

```csharp
public interface IAcquisitionBoundService
{
    bool RequiresMqttConnection { get; }
    void Start();
    void Stop();
}
```

- `RequiresMqttConnection`：Coordinator 据此判断 `CanRun` 公式
- `Start()` / `Stop()`：线程安全幂等，实现方自行保证

## Acceptance criteria

- [ ] 接口编译通过，位于 `WebAPI/Service/` 命名空间
- [ ] 不影响现有编译
