using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WebAPI.Models;
using SharedModels;
using WebAPI.Service;
using Xunit;

namespace WebAPI.Tests;

public class DetectionPublisherServiceTests
{
    /// <summary>
    /// IOptionsMonitor test double
    /// </summary>
    private sealed class TestOptionsMonitor<T> : IOptionsMonitor<T> where T : class, new()
    {
        public T CurrentValue { get; }
        public TestOptionsMonitor(T value) => CurrentValue = value;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private static DetectionPublisherService CreateService()
    {
        var stateService = new SystemStateService(NullLogger<SystemStateService>.Instance);
        var eventPublisher = new MqttEventPublisher(
            stateService,
            new TestOptionsMonitor<MqttSettings>(new MqttSettings { MachineId = "test" }),
            NullLogger<MqttEventPublisher>.Instance);
        // MqttClient is left null — loop skips publish, doesn't crash

        return new DetectionPublisherService(
            eventPublisher,
            new TestOptionsMonitor<MqttSettings>(new MqttSettings { MachineId = "test" }),
            NullLogger<DetectionPublisherService>.Instance);
    }

    [Fact]
    public void RequiresMqttConnection_ReturnsTrue()
    {
        var service = CreateService();
        Assert.True(service.RequiresMqttConnection);
    }

    [Fact]
    public void Start_IsIdempotent()
    {
        var service = CreateService();
        service.Start();
        service.Start();
        service.Start();
        // No exception = pass
        service.Stop();
    }

    [Fact]
    public void Stop_IsIdempotent()
    {
        var service = CreateService();
        service.Start();
        service.Stop();
        service.Stop();
        service.Stop();
        // No exception = pass
    }

    [Fact]
    public void Stop_WithoutStart_DoesNotThrow()
    {
        var service = CreateService();
        service.Stop();
    }

    [Fact]
    public async Task Start_ProcessesAlerts_AndStop_CompletesCleanly()
    {
        var service = CreateService();

        service.Start();
        service.OnAlertReceived(new DetectionAlertDto
        {
            AlarmType = "SIGNAL_OBSTRUCTION", Severity = "warning",
            Timestamp = 1, CH1 = 0.001, CH2 = 2.45
        });
        service.OnAlertReceived(new DetectionAlertDto
        {
            AlarmType = "SIGNAL_OBSTRUCTION", Severity = "warning",
            Timestamp = 2, CH1 = 0.002, CH2 = 2.46
        });

        // Give the background loop time to drain
        await Task.Delay(200);

        service.Stop();
        // No exception + no hang = pass
    }
}
