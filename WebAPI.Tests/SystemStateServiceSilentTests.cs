using Microsoft.Extensions.Logging.Abstractions;
using WebAPI.Models;
using WebAPI.Service;
using Xunit;

namespace WebAPI.Tests;

public class SystemStateServiceSilentTests
{
    private sealed class MockMqttPublisher : IMqttEventPublisher
    {
        public int PublishCallCount { get; private set; }
        public Task PublishStateChangedAsync(string eventType, string source, string reason, string message)
        {
            PublishCallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class MockSignalRPublisher : ISignalRHubPublisher
    {
        public int PublishCallCount { get; private set; }
        public Task PublishStateChangedAsync(string eventType, string source, string reason, string message)
        {
            PublishCallCount++;
            return Task.CompletedTask;
        }
    }

    private SystemStateService CreateService(MockMqttPublisher mqtt, MockSignalRPublisher signalR)
    {
        return new SystemStateService(
            NullLogger<SystemStateService>.Instance,
            new Lazy<IMqttEventPublisher>(() => mqtt),
            signalR);
    }

    [Fact]
    public void UpdateCollectorStateSilent_UpdatesCache()
    {
        var mqtt = new MockMqttPublisher();
        var signalR = new MockSignalRPublisher();
        var service = CreateService(mqtt, signalR);

        service.UpdateCollectorStateSilent(_ => new CollectorStateDto
        {
            ProcessConnected = true,
            DeviceOpened = true,
            Acquiring = false,
            Handle = 42,
            LastMessage = "test"
        });

        var state = service.GetCollectorState();
        Assert.True(state.ProcessConnected);
        Assert.True(state.DeviceOpened);
        Assert.Equal(42, state.Handle);
        Assert.Equal("test", state.LastMessage);
    }

    [Fact]
    public void UpdateCollectorStateSilent_DoesNotPublishToMqtt()
    {
        var mqtt = new MockMqttPublisher();
        var signalR = new MockSignalRPublisher();
        var service = CreateService(mqtt, signalR);

        service.UpdateCollectorStateSilent(_ => new CollectorStateDto
        {
            ProcessConnected = true, DeviceOpened = false,
            Acquiring = false, Handle = 0, LastMessage = ""
        });

        Assert.Equal(0, mqtt.PublishCallCount);
    }

    [Fact]
    public void UpdateCollectorStateSilent_DoesNotPublishToSignalR()
    {
        var mqtt = new MockMqttPublisher();
        var signalR = new MockSignalRPublisher();
        var service = CreateService(mqtt, signalR);

        service.UpdateCollectorStateSilent(_ => new CollectorStateDto
        {
            ProcessConnected = true, DeviceOpened = false,
            Acquiring = false, Handle = 0, LastMessage = ""
        });

        Assert.Equal(0, signalR.PublishCallCount);
    }

    [Fact]
    public void UpdateLaserStateSilent_UpdatesCache()
    {
        var mqtt = new MockMqttPublisher();
        var signalR = new MockSignalRPublisher();
        var service = CreateService(mqtt, signalR);

        service.UpdateLaserStateSilent(_ => new LaserStateDto
        {
            SerialConnected = true,
            EmissionOn = true,
            PortName = "COM3",
            LastMessage = "laser on"
        });

        var state = service.GetLaserState();
        Assert.True(state.SerialConnected);
        Assert.True(state.EmissionOn);
        Assert.Equal("COM3", state.PortName);
        Assert.Equal("laser on", state.LastMessage);
    }

    [Fact]
    public void UpdateLaserStateSilent_DoesNotPublish()
    {
        var mqtt = new MockMqttPublisher();
        var signalR = new MockSignalRPublisher();
        var service = CreateService(mqtt, signalR);

        service.UpdateLaserStateSilent(_ => new LaserStateDto
        {
            SerialConnected = true, EmissionOn = false,
            PortName = "", LastMessage = ""
        });

        Assert.Equal(0, mqtt.PublishCallCount);
        Assert.Equal(0, signalR.PublishCallCount);
    }

    [Fact]
    public void UpdateCollectorStateSilent_AcquiringChanged_FiresInternalEvent()
    {
        var mqtt = new MockMqttPublisher();
        var signalR = new MockSignalRPublisher();
        var service = CreateService(mqtt, signalR);
        var fired = false;
        var lastValue = false;
        service.AcquiringStateChanged += v => { fired = true; lastValue = v; };

        service.UpdateCollectorStateSilent(_ => new CollectorStateDto
        {
            ProcessConnected = true, DeviceOpened = true,
            Acquiring = true, Handle = 0, LastMessage = ""
        });

        Assert.True(fired);
        Assert.True(lastValue);
    }
}
