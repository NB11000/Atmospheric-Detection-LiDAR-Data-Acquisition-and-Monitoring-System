using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WebAPI.Service;
using Xunit;

namespace WebAPI.Tests;

public class AcquisitionLifecycleCoordinatorTests
{
    private sealed class SpyService : IAcquisitionBoundService
    {
        public bool RequiresMqttConnection { get; }
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }

        public SpyService(bool requiresMqtt)
        {
            RequiresMqttConnection = requiresMqtt;
        }

        public void Start() => StartCount++;
        public void Stop() => StopCount++;
    }

    [Fact]
    public void AcquiringStarts_NonMqttServiceReceivesStart()
    {
        var spy = new SpyService(requiresMqtt: false);
        var systemState = new SystemStateService(NullLogger<SystemStateService>.Instance);
        var coordinator = new AcquisitionLifecycleCoordinator(
            new[] { spy }, systemState, NullLogger<AcquisitionLifecycleCoordinator>.Instance);

        systemState.UpdateCollectorStateSilent(s =>
        {
            s.Acquiring = true;
            return s;
        });

        Assert.Equal(1, spy.StartCount);
        Assert.Equal(0, spy.StopCount);
    }

    [Fact]
    public void AcquiringStarts_WithoutMqtt_MqttServiceNotStarted()
    {
        var spy = new SpyService(requiresMqtt: true);
        var systemState = new SystemStateService(NullLogger<SystemStateService>.Instance);
        var coordinator = new AcquisitionLifecycleCoordinator(
            new[] { spy }, systemState, NullLogger<AcquisitionLifecycleCoordinator>.Instance);

        systemState.UpdateCollectorStateSilent(s =>
        {
            s.Acquiring = true;
            return s;
        });

        Assert.Equal(0, spy.StartCount);
        Assert.Equal(1, spy.StopCount);
    }

    [Fact]
    public void MqttConnects_WhileAcquiring_MqttServiceStarts()
    {
        var spy = new SpyService(requiresMqtt: true);
        var systemState = new SystemStateService(NullLogger<SystemStateService>.Instance);
        var coordinator = new AcquisitionLifecycleCoordinator(
            new[] { spy }, systemState, NullLogger<AcquisitionLifecycleCoordinator>.Instance);

        // 先开始采集（MQTT 未连接 → MqttService 停止）
        systemState.UpdateCollectorStateSilent(s => { s.Acquiring = true; return s; });
        Assert.Equal(0, spy.StartCount);
        Assert.Equal(1, spy.StopCount);

        // MQTT 连接 → 应该启动
        systemState.UpdateMqttConnectionState(true);

        Assert.Equal(1, spy.StartCount);
        Assert.Equal(1, spy.StopCount);
    }

    [Fact]
    public void AcquiringStops_AllServicesStop()
    {
        var spyNoMqtt = new SpyService(requiresMqtt: false);
        var spyMqtt = new SpyService(requiresMqtt: true);
        var systemState = new SystemStateService(NullLogger<SystemStateService>.Instance);
        var coordinator = new AcquisitionLifecycleCoordinator(
            new IAcquisitionBoundService[] { spyNoMqtt, spyMqtt }, systemState,
            NullLogger<AcquisitionLifecycleCoordinator>.Instance);

        // 采集开始 + MQTT 连接 → 两个都启动
        systemState.UpdateMqttConnectionState(true);
        systemState.UpdateCollectorStateSilent(s => { s.Acquiring = true; return s; });

        Assert.Equal(1, spyNoMqtt.StartCount);
        Assert.Equal(1, spyMqtt.StartCount);

        // 采集停止 → 两个都停
        systemState.UpdateCollectorStateSilent(s => { s.Acquiring = false; return s; });

        // 初始状态(采集未开始+MQTT未连接)调过一次 Stop，停止时再调一次 = 2
        Assert.Equal(2, spyNoMqtt.StopCount);
        Assert.Equal(2, spyMqtt.StopCount);
    }

    [Fact]
    public void MqttDisconnects_OnlyMqttServiceStops()
    {
        var spyNoMqtt = new SpyService(requiresMqtt: false);
        var spyMqtt = new SpyService(requiresMqtt: true);
        var systemState = new SystemStateService(NullLogger<SystemStateService>.Instance);
        var coordinator = new AcquisitionLifecycleCoordinator(
            new IAcquisitionBoundService[] { spyNoMqtt, spyMqtt }, systemState,
            NullLogger<AcquisitionLifecycleCoordinator>.Instance);

        // 采集开始（无 MQTT）→ 仅 non-MQTT 启动
        systemState.UpdateCollectorStateSilent(s => { s.Acquiring = true; return s; });
        Assert.Equal(1, spyNoMqtt.StartCount);
        Assert.Equal(0, spyMqtt.StartCount);

        // MQTT 连接 → MQTT 服务启动
        systemState.UpdateMqttConnectionState(true);
        Assert.Equal(1, spyMqtt.StartCount);

        // MQTT 断连 → MQTT 服务停，non-MQTT 继续
        systemState.UpdateMqttConnectionState(false);

        Assert.Equal(2, spyMqtt.StopCount);   // 初始 + 断连
        Assert.Equal(0, spyNoMqtt.StopCount); // 从未停止
    }

    [Fact]
    public void SameState_NoDuplicateCalls()
    {
        var spy = new SpyService(requiresMqtt: false);
        var systemState = new SystemStateService(NullLogger<SystemStateService>.Instance);
        var coordinator = new AcquisitionLifecycleCoordinator(
            new[] { spy }, systemState, NullLogger<AcquisitionLifecycleCoordinator>.Instance);

        // 第一次启动
        systemState.UpdateCollectorStateSilent(s => { s.Acquiring = true; return s; });
        Assert.Equal(1, spy.StartCount);

        // 重复触发 Acquiring=true（不应再调 Start）
        systemState.UpdateCollectorStateSilent(s => { s.Acquiring = true; return s; });

        Assert.Equal(1, spy.StartCount);
    }
}
