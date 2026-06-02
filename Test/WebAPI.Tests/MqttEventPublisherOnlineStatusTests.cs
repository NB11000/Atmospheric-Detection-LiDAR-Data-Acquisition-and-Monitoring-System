using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Diagnostics.PacketInspection;
using MQTTnet.Protocol;
using SharedModels;
using WebAPI.Service;
using Xunit;

namespace WebAPI.Tests;

public class MqttEventPublisherOnlineStatusTests
{
    private sealed class TestOptionsMonitor<T> : IOptionsMonitor<T> where T : class, new()
    {
        public T CurrentValue { get; }
        public TestOptionsMonitor(T value) => CurrentValue = value;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class MockMqttClient : IMqttClient
    {
        public MqttApplicationMessage? LastPublishedMessage { get; private set; }
        public int PublishCallCount { get; private set; }
        public bool IsConnected { get; set; }

        public MqttClientOptions Options => null!;

        public event Func<MqttApplicationMessageReceivedEventArgs, Task>? ApplicationMessageReceivedAsync;
        public event Func<MqttClientConnectedEventArgs, Task>? ConnectedAsync;
        public event Func<MqttClientConnectingEventArgs, Task>? ConnectingAsync;
        public event Func<MqttClientDisconnectedEventArgs, Task>? DisconnectedAsync;
        public event Func<InspectMqttPacketEventArgs, Task>? InspectPacketAsync;

        public async Task<MqttClientConnectResult> ConnectAsync(MqttClientOptions options, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public async Task DisconnectAsync(MqttClientDisconnectOptions options, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public async Task PingAsync(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public async Task<MqttClientPublishResult> PublishAsync(MqttApplicationMessage applicationMessage, CancellationToken cancellationToken = default)
        {
            LastPublishedMessage = applicationMessage;
            PublishCallCount++;
            return await Task.FromResult(new MqttClientPublishResult(null, MqttClientPublishReasonCode.Success, null, null));
        }

        public async Task SendEnhancedAuthenticationExchangeDataAsync(MqttEnhancedAuthenticationExchangeData data, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public async Task<MqttClientSubscribeResult> SubscribeAsync(MqttClientSubscribeOptions options, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public async Task<MqttClientUnsubscribeResult> UnsubscribeAsync(MqttClientUnsubscribeOptions options, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
        }
    }

    private static MqttEventPublisher CreatePublisher(IMqttClient? client = null)
    {
        var stateService = new SystemStateService(NullLogger<SystemStateService>.Instance);
        var publisher = new MqttEventPublisher(
            stateService,
            new TestOptionsMonitor<MqttSettings>(new MqttSettings { MachineId = "test-device-01" }),
            NullLogger<MqttEventPublisher>.Instance);
        publisher.MqttClient = client;
        return publisher;
    }

    [Fact]
    public void WillPayload_HasSixFields_WithCorrectValues()
    {
        var payloadBytes = MqttRpcBackgroundService.BuildWillPayloadBytes();
        var json = Encoding.UTF8.GetString(payloadBytes);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("status", out var status));
        Assert.Equal("offline", status.GetString());

        Assert.True(root.TryGetProperty("ts", out var ts));
        Assert.Equal(0, ts.GetInt64());

        Assert.True(root.TryGetProperty("eventType", out var eventType));
        Assert.Equal("process_crashed", eventType.GetString());

        Assert.True(root.TryGetProperty("source", out var source));
        Assert.Equal("mqtt_broker", source.GetString());

        Assert.True(root.TryGetProperty("message", out var message));
        Assert.Equal("设备已离线", message.GetString());

        Assert.True(root.TryGetProperty("timestamp", out var timestamp));
        Assert.Equal("0001-01-01T00:00:00Z", timestamp.GetString());

        Assert.Equal(6, root.EnumerateObject().Count());
    }

    [Fact]
    public async Task PublishDeviceOnlineAsync_MqttClientIsNull_DoesNotThrow()
    {
        var publisher = CreatePublisher(null);

        await publisher.PublishDeviceOnlineAsync();
    }

    [Fact]
    public async Task PublishDeviceOfflineAsync_MqttClientIsNull_DoesNotThrow()
    {
        var publisher = CreatePublisher(null);

        await publisher.PublishDeviceOfflineAsync();
    }

    [Fact]
    public async Task PublishDeviceOnlineAsync_MqttClientNotConnected_DoesNotThrow()
    {
        var mock = new MockMqttClient { IsConnected = false };
        var publisher = CreatePublisher(mock);

        await publisher.PublishDeviceOnlineAsync();

        Assert.Equal(0, mock.PublishCallCount);
    }

    [Fact]
    public async Task PublishDeviceOfflineAsync_MqttClientNotConnected_DoesNotThrow()
    {
        var mock = new MockMqttClient { IsConnected = false };
        var publisher = CreatePublisher(mock);

        await publisher.PublishDeviceOfflineAsync();

        Assert.Equal(0, mock.PublishCallCount);
    }

    [Fact]
    public async Task PublishDeviceOnlineAsync_PublishesToCorrectTopic()
    {
        var mock = new MockMqttClient { IsConnected = true };
        var publisher = CreatePublisher(mock);

        await publisher.PublishDeviceOnlineAsync();

        Assert.Equal(1, mock.PublishCallCount);
        Assert.NotNull(mock.LastPublishedMessage);
        Assert.Equal("daq/test-device-01/events/will", mock.LastPublishedMessage!.Topic);
    }

    [Fact]
    public async Task PublishDeviceOnlineAsync_PublishesWithRetainAndQoS1()
    {
        var mock = new MockMqttClient { IsConnected = true };
        var publisher = CreatePublisher(mock);

        await publisher.PublishDeviceOnlineAsync();

        Assert.NotNull(mock.LastPublishedMessage);
        Assert.True(mock.LastPublishedMessage!.Retain);
        Assert.Equal(MqttQualityOfServiceLevel.AtLeastOnce, mock.LastPublishedMessage.QualityOfServiceLevel);
    }

    [Fact]
    public async Task PublishDeviceOnlineAsync_PayloadHasCorrectValues()
    {
        var mock = new MockMqttClient { IsConnected = true };
        var publisher = CreatePublisher(mock);

        await publisher.PublishDeviceOnlineAsync();

        Assert.NotNull(mock.LastPublishedMessage);
        var payload = Encoding.UTF8.GetString(mock.LastPublishedMessage!.Payload);

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        Assert.Equal("online", root.GetProperty("status").GetString());
        Assert.Equal("device_online", root.GetProperty("eventType").GetString());
        Assert.Equal("device", root.GetProperty("source").GetString());
        Assert.Equal("设备已上线", root.GetProperty("message").GetString());

        var ts = root.GetProperty("ts").GetInt64();
        var expectedTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Assert.True(ts > 0 && ts <= expectedTs + 1000);
    }

    [Fact]
    public async Task PublishDeviceOfflineAsync_PublishesToCorrectTopic()
    {
        var mock = new MockMqttClient { IsConnected = true };
        var publisher = CreatePublisher(mock);

        await publisher.PublishDeviceOfflineAsync();

        Assert.Equal(1, mock.PublishCallCount);
        Assert.NotNull(mock.LastPublishedMessage);
        Assert.Equal("daq/test-device-01/events/will", mock.LastPublishedMessage!.Topic);
    }

    [Fact]
    public async Task PublishDeviceOfflineAsync_PublishesWithRetainAndQoS1()
    {
        var mock = new MockMqttClient { IsConnected = true };
        var publisher = CreatePublisher(mock);

        await publisher.PublishDeviceOfflineAsync();

        Assert.NotNull(mock.LastPublishedMessage);
        Assert.True(mock.LastPublishedMessage!.Retain);
        Assert.Equal(MqttQualityOfServiceLevel.AtLeastOnce, mock.LastPublishedMessage.QualityOfServiceLevel);
    }

    [Fact]
    public async Task PublishDeviceOfflineAsync_PayloadHasCorrectValues()
    {
        var mock = new MockMqttClient { IsConnected = true };
        var publisher = CreatePublisher(mock);

        await publisher.PublishDeviceOfflineAsync();

        Assert.NotNull(mock.LastPublishedMessage);
        var payload = Encoding.UTF8.GetString(mock.LastPublishedMessage!.Payload);

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        Assert.Equal("offline", root.GetProperty("status").GetString());
        Assert.Equal("device_offline", root.GetProperty("eventType").GetString());
        Assert.Equal("device", root.GetProperty("source").GetString());
        Assert.Equal("设备正常下线", root.GetProperty("message").GetString());

        var ts = root.GetProperty("ts").GetInt64();
        var expectedTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Assert.True(ts > 0 && ts <= expectedTs + 1000);
    }
}
