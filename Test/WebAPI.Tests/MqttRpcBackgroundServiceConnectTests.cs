using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Diagnostics.PacketInspection;
using MQTTnet.Protocol;
using WebAPI.Models;
using SharedModels;
using WebAPI.Service;
using WebAPI.MqttRpc;
using Xunit;

namespace WebAPI.Tests;

public class MqttRpcBackgroundServiceConnectTests
{
    private sealed class TestOptionsMonitor<T> : IOptionsMonitor<T> where T : class, new()
    {
        public T CurrentValue { get; }
        public TestOptionsMonitor(T value) => CurrentValue = value;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class CallOrderMockMqttClient : IMqttClient
    {
        public List<string> CallOrder { get; } = new();
        public bool IsConnected { get; set; } = true;

        public MqttClientOptions Options => null!;

        public event Func<MqttApplicationMessageReceivedEventArgs, Task>? ApplicationMessageReceivedAsync;
        public event Func<MqttClientConnectedEventArgs, Task>? ConnectedAsync;
        public event Func<MqttClientConnectingEventArgs, Task>? ConnectingAsync;
        public event Func<MqttClientDisconnectedEventArgs, Task>? DisconnectedAsync;
        public event Func<InspectMqttPacketEventArgs, Task>? InspectPacketAsync;

        public async Task<MqttClientConnectResult> ConnectAsync(MqttClientOptions options, CancellationToken cancellationToken = default)
        {
            CallOrder.Add("Connect");
            var result = default(MqttClientConnectResult);
            return await Task.FromResult(result);
        }

        public async Task DisconnectAsync(MqttClientDisconnectOptions options, CancellationToken cancellationToken = default)
        {
            CallOrder.Add("Disconnect");
            await Task.CompletedTask;
        }

        public async Task PingAsync(CancellationToken cancellationToken = default)
            => await Task.CompletedTask;

        public async Task<MqttClientPublishResult> PublishAsync(MqttApplicationMessage applicationMessage, CancellationToken cancellationToken = default)
        {
            CallOrder.Add($"Publish:{applicationMessage.Topic}");
            return await Task.FromResult(new MqttClientPublishResult(null, MqttClientPublishReasonCode.Success, null, null));
        }

        public async Task SendEnhancedAuthenticationExchangeDataAsync(MqttEnhancedAuthenticationExchangeData data, CancellationToken cancellationToken = default)
            => await Task.CompletedTask;

        public async Task<MqttClientSubscribeResult> SubscribeAsync(MqttClientSubscribeOptions options, CancellationToken cancellationToken = default)
        {
            CallOrder.Add("Subscribe");
            return await Task.FromResult(new MqttClientSubscribeResult(0, Array.Empty<MqttClientSubscribeResultItem>(), null, null));
        }

        public async Task<MqttClientUnsubscribeResult> UnsubscribeAsync(MqttClientUnsubscribeOptions options, CancellationToken cancellationToken = default)
            => await Task.FromResult(new MqttClientUnsubscribeResult(0, Array.Empty<MqttClientUnsubscribeResultItem>(), null, null));

        public void Dispose() { }
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
        CallOrderMockMqttClient mockClient,
        MqttEventPublisher eventPublisher,
        SystemStateService stateService,
        TestOptionsMonitor<MqttSettings> optionsMonitor)
    {
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
            optionsMonitor,
            eventPublisher,
            stateService,
            NullLogger<MqttRpcBackgroundService>.Instance);

        typeof(MqttRpcBackgroundService)
            .GetField("_mqttClient", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(service, mockClient);

        eventPublisher.MqttClient = mockClient;

        return service;
    }

    [Fact]
    public async Task ConnectAsync_CallsPublishDeviceOnline_BetweenSubscribeAndStateUpdate()
    {
        var mockClient = new CallOrderMockMqttClient { IsConnected = true };
        var stateService = new SystemStateService(NullLogger<SystemStateService>.Instance);
        var optionsMonitor = new TestOptionsMonitor<MqttSettings>(new MqttSettings
        {
            MachineId = "test-01",
            BrokerHost = "localhost",
            BrokerPort = 1883
        });
        var eventPublisher = new MqttEventPublisher(
            stateService,
            optionsMonitor,
            NullLogger<MqttEventPublisher>.Instance);

        var service = CreateService(mockClient, eventPublisher, stateService, optionsMonitor);

        int? stateUpdateAfterCount = null;
        stateService.MqttConnectionStateChanged += _ =>
        {
            stateUpdateAfterCount = mockClient.CallOrder.Count;
        };

        await service.ConnectAsync(CancellationToken.None);

        var subscribeIndex = mockClient.CallOrder.FindIndex(c => c == "Subscribe");
        Assert.True(subscribeIndex >= 0, "Subscribe should have been called");

        var publishIndex = mockClient.CallOrder.FindIndex(c => c == "Publish:daq/test-01/events/will");
        Assert.True(publishIndex >= 0,
            $"PublishDeviceOnlineAsync should publish to events/will. CallOrder: [{string.Join(", ", mockClient.CallOrder)}]");

        Assert.True(subscribeIndex < publishIndex,
            $"Subscribe (index {subscribeIndex}) should happen before PublishDeviceOnline (index {publishIndex})");

        Assert.NotNull(stateUpdateAfterCount);
        Assert.True(publishIndex < stateUpdateAfterCount!.Value,
            $"Publish (index {publishIndex}) should happen before UpdateMqttConnectionState (after {stateUpdateAfterCount} calls). CallOrder: [{string.Join(", ", mockClient.CallOrder)}]");

        var connectIndex = mockClient.CallOrder.FindIndex(c => c == "Connect");
        Assert.True(connectIndex >= 0);
        Assert.True(connectIndex < subscribeIndex,
            $"Connect (index {connectIndex}) should happen before Subscribe (index {subscribeIndex})");
    }
}
