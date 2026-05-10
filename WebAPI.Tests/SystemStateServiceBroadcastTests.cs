using Microsoft.Extensions.Logging.Abstractions;
using WebAPI.Models;
using WebAPI.Service;
using Xunit;

namespace WebAPI.Tests;

public class SystemStateServiceBroadcastTests
{
    private sealed class MockMqttPublisher : IMqttEventPublisher
    {
        public string LastEventType { get; private set; } = "";
        public string LastSource { get; private set; } = "";
        public string LastReason { get; private set; } = "";
        public string LastMessage { get; private set; } = "";
        public int PublishCallCount { get; private set; }

        public Task PublishStateChangedAsync(string eventType, string source, string reason, string message)
        {
            LastEventType = eventType;
            LastSource = source;
            LastReason = reason;
            LastMessage = message;
            PublishCallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class MockSignalRPublisher : ISignalRHubPublisher
    {
        public string LastEventType { get; private set; } = "";
        public string LastSource { get; private set; } = "";
        public string LastReason { get; private set; } = "";
        public string LastMessage { get; private set; } = "";
        public int PublishCallCount { get; private set; }

        public Task PublishStateChangedAsync(string eventType, string source, string reason, string message)
        {
            LastEventType = eventType;
            LastSource = source;
            LastReason = reason;
            LastMessage = message;
            PublishCallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingMqttPublisher : IMqttEventPublisher
    {
        public Task PublishStateChangedAsync(string eventType, string source, string reason, string message)
            => throw new InvalidOperationException("MQTT publish failed");
    }

    private SystemStateService CreateService(IMqttEventPublisher mqtt, ISignalRHubPublisher signalR)
    {
        return new SystemStateService(
            NullLogger<SystemStateService>.Instance,
            new Lazy<IMqttEventPublisher>(() => mqtt),
            signalR);
    }

    [Fact]
    public void UpdateCollectorStateAndBroadcast_UpdatesCacheAndPublishesBothChannels()
    {
        var mqtt = new MockMqttPublisher();
        var signalR = new MockSignalRPublisher();
        var service = CreateService(mqtt, signalR);

        service.UpdateCollectorStateAndBroadcast(_ => new CollectorStateDto
        {
            ProcessConnected = true, DeviceOpened = true,
            Acquiring = false, Handle = 42, LastMessage = "connected"
        }, "collector_connected", "gRPC 连接已建立");

        var state = service.GetCollectorState();
        Assert.True(state.ProcessConnected);
        Assert.Equal(42, state.Handle);

        Assert.Equal(1, mqtt.PublishCallCount);
        Assert.Equal("collector_connected", mqtt.LastEventType);
        Assert.Equal("collector", mqtt.LastSource);
        Assert.Equal("gRPC 连接已建立", mqtt.LastReason);

        Assert.Equal(1, signalR.PublishCallCount);
        Assert.Equal("collector_connected", signalR.LastEventType);
        Assert.Equal("collector", signalR.LastSource);
    }

    [Fact]
    public void UpdateLaserStateAndBroadcast_PublishesBothChannels()
    {
        var mqtt = new MockMqttPublisher();
        var signalR = new MockSignalRPublisher();
        var service = CreateService(mqtt, signalR);

        service.UpdateLaserStateAndBroadcast(_ => new LaserStateDto
        {
            SerialConnected = true, EmissionOn = true,
            PortName = "COM3", LastMessage = "laser on"
        }, "laser_on", "激光已开启");

        Assert.Equal("laser", mqtt.LastSource);
        Assert.Equal("laser_on", signalR.LastEventType);
        Assert.Equal(1, mqtt.PublishCallCount);
        Assert.Equal(1, signalR.PublishCallCount);
    }

    [Fact]
    public void ResetCollectorStateAndBroadcast_ResetsAndPublishes()
    {
        var mqtt = new MockMqttPublisher();
        var signalR = new MockSignalRPublisher();
        var service = CreateService(mqtt, signalR);

        service.ResetCollectorStateAndBroadcast("采集子进程已断开");

        var state = service.GetCollectorState();
        Assert.False(state.ProcessConnected);
        Assert.False(state.DeviceOpened);
        Assert.False(state.Acquiring);
        Assert.Equal(0, state.Handle);

        Assert.Equal(1, mqtt.PublishCallCount);
        Assert.Equal("collector_disconnected", mqtt.LastEventType);
        Assert.Equal(1, signalR.PublishCallCount);
        Assert.Equal("collector_disconnected", signalR.LastEventType);
    }

    [Fact]
    public void Broadcast_AcquiringChanged_FiresInternalEvent()
    {
        var mqtt = new MockMqttPublisher();
        var signalR = new MockSignalRPublisher();
        var service = CreateService(mqtt, signalR);
        var fired = false;
        var lastValue = false;
        service.AcquiringStateChanged += v => { fired = true; lastValue = v; };

        service.UpdateCollectorStateAndBroadcast(_ => new CollectorStateDto
        {
            ProcessConnected = true, DeviceOpened = true,
            Acquiring = true, Handle = 0, LastMessage = ""
        }, "acquisition_started", "采集已启动");

        Assert.True(fired);
        Assert.True(lastValue);
    }

    [Fact]
    public void Broadcast_PublishFailure_DoesNotCorruptState()
    {
        var mqtt = new ThrowingMqttPublisher();
        var signalR = new MockSignalRPublisher();
        var service = CreateService(mqtt, signalR);

        service.UpdateCollectorStateAndBroadcast(_ => new CollectorStateDto
        {
            ProcessConnected = true, DeviceOpened = true,
            Acquiring = false, Handle = 42, LastMessage = "ok"
        }, "collector_connected", "test");

        var state = service.GetCollectorState();
        Assert.True(state.ProcessConnected);
        Assert.Equal(42, state.Handle);
        // SignalR still pushed (different channel)
        Assert.Equal(1, signalR.PublishCallCount);
    }
}
