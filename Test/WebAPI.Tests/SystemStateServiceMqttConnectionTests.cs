using Microsoft.Extensions.Logging.Abstractions;
using WebAPI.Service;
using Xunit;

namespace WebAPI.Tests;

public class SystemStateServiceMqttConnectionTests
{
    private sealed class MockMqttPublisher : IMqttEventPublisher
    {
        public string LastEventType { get; private set; } = "";
        public int PublishCallCount { get; private set; }
        public Task PublishStateChangedAsync(string eventType, string source, string reason, string message)
        {
            LastEventType = eventType; PublishCallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class MockSignalRPublisher : ISignalRHubPublisher
    {
        public string LastEventType { get; private set; } = "";
        public int PublishCallCount { get; private set; }
        public Task PublishStateChangedAsync(string eventType, string source, string reason, string message)
        {
            LastEventType = eventType; PublishCallCount++;
            return Task.CompletedTask;
        }
    }

    private SystemStateService CreateService(IMqttEventPublisher mqtt, ISignalRHubPublisher signalR)
    {
        return new SystemStateService(
            NullLogger<SystemStateService>.Instance,
            new Lazy<IMqttEventPublisher>(() => mqtt),
            signalR);
    }

    [Fact]
    public void UpdateMqttConnectionState_SameValue_NoOp()
    {
        var mqtt = new MockMqttPublisher();
        var signalR = new MockSignalRPublisher();
        var service = CreateService(mqtt, signalR);
        var eventCount = 0;
        service.MqttConnectionStateChanged += _ => eventCount++;

        service.UpdateMqttConnectionState(false); // initial: false→false
        Assert.Equal(0, eventCount);
        Assert.Equal(0, mqtt.PublishCallCount);
        Assert.Equal(0, signalR.PublishCallCount);
    }

    [Fact]
    public void UpdateMqttConnectionState_True_FiresEventAndPublishesBoth()
    {
        var mqtt = new MockMqttPublisher();
        var signalR = new MockSignalRPublisher();
        var service = CreateService(mqtt, signalR);
        var fired = false; var lastValue = false;
        service.MqttConnectionStateChanged += v => { fired = true; lastValue = v; };

        service.UpdateMqttConnectionState(true);

        Assert.True(fired);
        Assert.True(lastValue);
        Assert.Equal(1, mqtt.PublishCallCount);
        Assert.Equal("mqtt_connected", mqtt.LastEventType);
        Assert.Equal(1, signalR.PublishCallCount);
        Assert.Equal("mqtt_connected", signalR.LastEventType);
    }

    [Fact]
    public void UpdateMqttConnectionState_False_FiresEventAndPublishesSignalROnly()
    {
        var mqtt = new MockMqttPublisher();
        var signalR = new MockSignalRPublisher();
        var service = CreateService(mqtt, signalR);
        // Connect first
        service.UpdateMqttConnectionState(true);

        var fired = false; var lastValue = false;
        service.MqttConnectionStateChanged += v => { fired = true; lastValue = v; };

        service.UpdateMqttConnectionState(false);

        Assert.True(fired);
        Assert.False(lastValue);
        // SignalR called for both connect and disconnect
        Assert.Equal("mqtt_disconnected", signalR.LastEventType);
        Assert.Equal(2, signalR.PublishCallCount);
        // MQTT: only called for connect, not for disconnect
        Assert.Equal("mqtt_connected", mqtt.LastEventType);
        Assert.Equal(1, mqtt.PublishCallCount);
    }
}
