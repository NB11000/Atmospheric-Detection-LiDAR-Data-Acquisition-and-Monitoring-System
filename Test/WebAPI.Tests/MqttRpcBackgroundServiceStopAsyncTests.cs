using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Diagnostics.PacketInspection;
using MQTTnet.Protocol;
using SharedModels;
using WebAPI.MqttRpc;
using WebAPI.Service;
using Xunit;

namespace WebAPI.Tests;

public class MqttRpcBackgroundServiceStopAsyncTests
{
    private sealed class TestOptionsMonitor<T> : IOptionsMonitor<T> where T : class, new()
    {
        public T CurrentValue { get; }
        public TestOptionsMonitor(T value) => CurrentValue = value;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class FakeMqttClient : IMqttClient
    {
        public bool IsConnected { get; set; }

        public int PublishCallCount { get; private set; }
        public MqttApplicationMessage? LastPublishedMessage { get; private set; }

        public int DisconnectCallCount { get; private set; }
        public int DisposeCallCount { get; private set; }

        public bool ConnectThrows { get; set; }
        public CancellationToken? ConnectCancellationToken { get; private set; }

        public MqttClientOptions Options => null!;

        public event Func<MqttApplicationMessageReceivedEventArgs, Task>? ApplicationMessageReceivedAsync;
        public event Func<MqttClientConnectedEventArgs, Task>? ConnectedAsync;
        public event Func<MqttClientConnectingEventArgs, Task>? ConnectingAsync;
        public event Func<MqttClientDisconnectedEventArgs, Task>? DisconnectedAsync;
        public event Func<InspectMqttPacketEventArgs, Task>? InspectPacketAsync;

        public async Task<MqttClientConnectResult> ConnectAsync(MqttClientOptions options, CancellationToken cancellationToken = default)
        {
            ConnectCancellationToken = cancellationToken;
            if (ConnectThrows) throw new InvalidOperationException("Fake connect failure");
            var result = default(MqttClientConnectResult);
            return await Task.FromResult(result);
        }

        public async Task DisconnectAsync(MqttClientDisconnectOptions options, CancellationToken cancellationToken = default)
        {
            DisconnectCallCount++;
            await Task.CompletedTask;
        }

        public async Task PingAsync(CancellationToken cancellationToken = default)
            => await Task.CompletedTask;

        public async Task<MqttClientPublishResult> PublishAsync(MqttApplicationMessage applicationMessage, CancellationToken cancellationToken = default)
        {
            LastPublishedMessage = applicationMessage;
            PublishCallCount++;
            return await Task.FromResult(new MqttClientPublishResult(null, MqttClientPublishReasonCode.Success, null, null));
        }

        public async Task SendEnhancedAuthenticationExchangeDataAsync(MqttEnhancedAuthenticationExchangeData data, CancellationToken cancellationToken = default)
            => await Task.CompletedTask;

        public async Task<MqttClientSubscribeResult> SubscribeAsync(MqttClientSubscribeOptions options, CancellationToken cancellationToken = default)
            => await Task.FromResult(new MqttClientSubscribeResult(0, Array.Empty<MqttClientSubscribeResultItem>(), null, null));

        public async Task<MqttClientUnsubscribeResult> UnsubscribeAsync(MqttClientUnsubscribeOptions options, CancellationToken cancellationToken = default)
            => await Task.FromResult(new MqttClientUnsubscribeResult(0, Array.Empty<MqttClientUnsubscribeResultItem>(), null, null));

        public void Dispose()
        {
            DisposeCallCount++;
        }
    }

    private sealed class FakeServiceProvider : IServiceProvider
    {
        private readonly Dictionary<Type, object> _services;

        public FakeServiceProvider(Dictionary<Type, object> services) => _services = services;

        public object? GetService(Type serviceType)
        {
            _services.TryGetValue(serviceType, out var instance);
            return instance;
        }
    }

    private static MqttRpcBackgroundService CreateService(
        out FakeMqttClient fakeClient,
        bool clientConnected = false)
    {
        fakeClient = new FakeMqttClient { IsConnected = clientConnected };

        var stateService = new SystemStateService(NullLogger<SystemStateService>.Instance);

        var publisher = new MqttEventPublisher(
            stateService,
            new TestOptionsMonitor<MqttSettings>(new MqttSettings { MachineId = "test-device-01" }),
            NullLogger<MqttEventPublisher>.Instance);
        publisher.MqttClient = fakeClient;

        var handlers = new Dictionary<Type, object>
        {
            [typeof(CollectorHandler)] = new CollectorHandler(null!, null!, null!, null!),
            [typeof(LaserHandler)] = new LaserHandler(null!, null!, null!, null!),
            [typeof(SystemHandler)] = new SystemHandler(null!, null!),
            [typeof(LogHandler)] = new LogHandler(NullLogger<LogHandler>.Instance),
            [typeof(ConfigHandler)] = new ConfigHandler(null!, null!),
        };
        var sp = new FakeServiceProvider(handlers);

        var service = new MqttRpcBackgroundService(
            sp,
            new TestOptionsMonitor<MqttSettings>(new MqttSettings { MachineId = "test-device-01" }),
            publisher,
            stateService,
            NullLogger<MqttRpcBackgroundService>.Instance);

        typeof(MqttRpcBackgroundService)
            .GetField("_mqttClient", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(service, fakeClient);

        return service;
    }

    // ═══════════════════════════════════════════════
    //  RED Test 1
    // ═══════════════════════════════════════════════
    [Fact]
    public async Task StopAsync_CancelsShutdownCts()
    {
        var service = CreateService(out _);
        var shutdownCtsField = typeof(MqttRpcBackgroundService)
            .GetField("_shutdownCts", BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(shutdownCtsField); // RED: field doesn't exist yet

        await service.StopAsync(CancellationToken.None);

        var cts = (CancellationTokenSource)shutdownCtsField.GetValue(service)!;
        Assert.True(cts.IsCancellationRequested);
    }
}
