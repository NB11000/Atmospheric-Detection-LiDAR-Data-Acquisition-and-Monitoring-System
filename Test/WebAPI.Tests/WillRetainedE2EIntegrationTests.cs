using System.Diagnostics;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using MQTTnet;
using MQTTnet.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace WebAPI.Tests;

public class WillRetainedE2EIntegrationTests : IAsyncDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _testMachineId;
    private readonly string _willTopic;
    private Process? _webApiProcess;
    private IMqttClient? _testSubscriber;
    private readonly List<WillEventMessage> _receivedMessages = new();
    private readonly SemaphoreSlim _messageSync = new(0, int.MaxValue);
    private readonly CancellationTokenSource _cts = new(TimeSpan.FromMinutes(3));

    private class WillEventMessage
    {
        public DateTime ReceivedAt { get; set; }
        public string Status { get; set; } = "";
        public long Ts { get; set; }
        public string EventType { get; set; } = "";
        public string Source { get; set; } = "";
        public string Message { get; set; } = "";
        public string Timestamp { get; set; } = "";
    }

    public WillRetainedE2EIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _testMachineId = $"test-e2e-{suffix}";
        _willTopic = $"daq/{_testMachineId}/events/will";
    }

    // ═══════════════════════════════════════════════
    //  Helper: read appsettings.json for broker config
    // ═══════════════════════════════════════════════
    private static IConfigurationRoot LoadAppSettings()
    {
        var appsettingsPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "WebAPI", "appsettings.json"));
        if (!File.Exists(appsettingsPath))
        {
            appsettingsPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "WebAPI", "appsettings.json"));
        }
        if (!File.Exists(appsettingsPath))
        {
            appsettingsPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "appsettings.json"));
        }

        return new ConfigurationBuilder()
            .AddJsonFile(appsettingsPath, optional: false, reloadOnChange: false)
            .Build();
    }

    // ═══════════════════════════════════════════════
    //  Helper: create test MQTT subscriber
    // ═══════════════════════════════════════════════
    private async Task<IMqttClient> CreateTestSubscriberAsync(IConfiguration config)
    {
        var factory = new MqttClientFactory();
        var client = factory.CreateMqttClient();

        client.ApplicationMessageReceivedAsync += e =>
        {
            var topic = e.ApplicationMessage.Topic;
            if (topic.Contains("events/will"))
            {
                var payload = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);
                try
                {
                    using var doc = JsonDocument.Parse(payload);
                    var root = doc.RootElement;
                    var msg = new WillEventMessage
                    {
                        ReceivedAt = DateTime.UtcNow,
                        Status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "",
                        Ts = root.TryGetProperty("ts", out var t) ? t.GetInt64() : -1,
                        EventType = root.TryGetProperty("eventType", out var et) ? et.GetString() ?? "" : "",
                        Source = root.TryGetProperty("source", out var src) ? src.GetString() ?? "" : "",
                        Message = root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "",
                        Timestamp = root.TryGetProperty("timestamp", out var ts) ? ts.GetString() ?? "" : ""
                    };

                    lock (_receivedMessages)
                    {
                        _receivedMessages.Add(msg);
                    }

                    _output.WriteLine($"[SUBSCRIBER] Received on {topic}: status={msg.Status} eventType={msg.EventType} ts={msg.Ts}");
                    _messageSync.Release();
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"[SUBSCRIBER] Parse error: {ex.Message}");
                }
            }
            return Task.CompletedTask;
        };

        var brokerHost = config["Mqtt:BrokerHost"] ?? "localhost";
        var brokerPort = int.Parse(config["Mqtt:BrokerPort"] ?? "1883");
        var useTls = bool.Parse(config["Mqtt:UseTls"] ?? "false");
        var username = config["Mqtt:Username"] ?? "";
        var password = config["Mqtt:Password"] ?? "";
        var caCertPath = config["Mqtt:CaCertificatePath"] ?? "";

        _output.WriteLine($"[SUBSCRIBER] Connecting to {brokerHost}:{brokerPort} TLS={useTls}");

        var optsBuilder = new MqttClientOptionsBuilder()
            .WithTcpServer(brokerHost, brokerPort)
            .WithClientId($"test-subscriber-{Guid.NewGuid():N}"[..23])
            .WithCleanSession(true)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(60))
            .WithTimeout(TimeSpan.FromSeconds(15));

        if (!string.IsNullOrEmpty(username))
            optsBuilder.WithCredentials(username, password);

        if (useTls)
        {
            optsBuilder.WithTlsOptions(tls =>
            {
                tls.UseTls();
                tls.WithSslProtocols(SslProtocols.Tls12 | SslProtocols.Tls13);
                tls.WithCertificateValidationHandler(_ => true);
            });
        }

        var result = await client.ConnectAsync(optsBuilder.Build(), _cts.Token);
        Assert.Equal(MqttClientConnectResultCode.Success, result.ResultCode);

        _output.WriteLine("[SUBSCRIBER] Connected to broker");

        await client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter("daq/+/events/will", MqttQualityOfServiceLevel.AtLeastOnce)
            .Build(), _cts.Token);

        _output.WriteLine("[SUBSCRIBER] Subscribed to daq/+/events/will");

        return client;
    }

    // ═══════════════════════════════════════════════
    //  Helper: start WebAPI process
    // ═══════════════════════════════════════════════
    private static Process StartWebApi(string machineId)
    {
        var projectDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "WebAPI"));

        if (!Directory.Exists(projectDir))
        {
            projectDir = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "WebAPI"));
        }

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{projectDir}\" --no-build -- Mqtt:MachineId={machineId}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = projectDir
        };

        psi.EnvironmentVariables["ASPNETCORE_ENVIRONMENT"] = "Development";
        psi.EnvironmentVariables["Mqtt__MachineId"] = machineId;

        var process = new Process { StartInfo = psi };
        process.Start();
        return process;
    }

    // ═══════════════════════════════════════════════
    //  Helper: wait for expected retained message
    // ═══════════════════════════════════════════════
    private async Task<WillEventMessage?> WaitForMessageAsync(
        Func<WillEventMessage, bool> predicate,
        TimeSpan timeout,
        string description)
    {
        var deadline = DateTime.UtcNow + timeout;
        _output.WriteLine($"[WAIT] {description} (timeout={timeout.TotalSeconds}s)");

        while (DateTime.UtcNow < deadline && !_cts.IsCancellationRequested)
        {
            lock (_receivedMessages)
            {
                var match = _receivedMessages.LastOrDefault(predicate);
                if (match != null)
                {
                    _output.WriteLine($"[WAIT] Found: status={match.Status} eventType={match.EventType} ts={match.Ts}");
                    return match;
                }
            }

            var remaining = deadline - DateTime.UtcNow;
            if (remaining > TimeSpan.Zero)
            {
                try
                {
                    await _messageSync.WaitAsync(remaining, _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _output.WriteLine($"[WAIT] Timeout waiting for: {description}");
        return null;
    }

    /// <summary>
    /// E2E Test: Start → online retained → kill → Will offline retained → restart → online retained
    ///
    /// Prerequisites:
    ///   - EMQX Cloud broker at z0d131fe.ala.cn-hangzhou.emqxsl.cn:8883 (configured in appsettings.json)
    ///   - Valid credentials in appsettings.json
    ///   - .NET 8.0 SDK
    ///
    /// Run:
    ///   dotnet test --filter "FullyQualifiedName~WillRetainedE2E" -l "console;verbosity=detailed"
    /// </summary>
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresBroker")]
    [Fact(Timeout = 180000)]
    public async Task WillRetainedE2E_CrashToWillToRestart_OnlineCycle()
    {
        var config = LoadAppSettings();
        var brokerHost = config["Mqtt:BrokerHost"] ?? "localhost";

        _output.WriteLine($"=== Will Retained E2E Test ===");
        _output.WriteLine($"Broker: {brokerHost}");
        _output.WriteLine($"MachineId: {_testMachineId}");
        _output.WriteLine($"Topic: {_willTopic}");

        // ── STEP 0: Connect test subscriber ────────────────────────
        _testSubscriber = await CreateTestSubscriberAsync(config);

        // Drain any retained message that arrived on subscribe (from previous test runs)
        lock (_receivedMessages)
        {
            _receivedMessages.Clear();
        }

        // ── STEP 1: Start WebAPI ───────────────────────────────────
        _output.WriteLine("[STEP 1] Starting WebAPI...");
        _webApiProcess = StartWebApi(_testMachineId);

        // ── CHECKPOINT 1 (10s): Assert received retained online ───
        var cp1 = await WaitForMessageAsync(
            m => m.Status == "online" && m.EventType == "device_online",
            TimeSpan.FromSeconds(12),
            "CP1: retained online (device_online)");

        Assert.NotNull(cp1);
        Assert.Equal("online", cp1!.Status);
        Assert.Equal("device_online", cp1.EventType);
        Assert.Equal("device", cp1.Source);
        Assert.Equal("设备已上线", cp1.Message);
        Assert.True(cp1.Ts > 0, $"Active publish ts should be > 0, got {cp1.Ts}");
        Assert.NotEqual("0001-01-01T00:00:00Z", cp1.Timestamp);

        _output.WriteLine("[CHECKPOINT 1 PASSED] Received online retained message");

        // ── STEP 2: Kill WebAPI process ────────────────────────────
        _output.WriteLine("[STEP 2] Killing WebAPI process...");
        try
        {
            _webApiProcess.Kill(entireProcessTree: true);
            await _webApiProcess.WaitForExitAsync(_cts.Token);
        }
        catch (Exception ex)
        {
            _output.WriteLine($"[STEP 2] Process kill/exit exception (may be expected): {ex.Message}");
        }
        _output.WriteLine("[STEP 2] WebAPI process killed");

        // ── CHECKPOINT 2 (90s): Assert received Will retained offline
        var cp2 = await WaitForMessageAsync(
            m => m.Status == "offline" && m.Ts == 0,
            TimeSpan.FromSeconds(95),
            "CP2: Will retained offline (process_crashed, ts=0)");

        Assert.NotNull(cp2);
        Assert.Equal("offline", cp2!.Status);
        Assert.Equal("process_crashed", cp2.EventType);
        Assert.Equal("mqtt_broker", cp2.Source);
        Assert.Equal("设备已离线", cp2.Message);
        Assert.Equal(0, cp2.Ts);
        Assert.Equal("0001-01-01T00:00:00Z", cp2.Timestamp);

        _output.WriteLine($"[CHECKPOINT 2 PASSED] Received Will retained offline message after {cp2.ReceivedAt:HH:mm:ss.fff}");

        // ── STEP 3: Restart WebAPI ─────────────────────────────────
        _output.WriteLine("[STEP 3] Restarting WebAPI...");
        _webApiProcess = StartWebApi(_testMachineId);

        // ── CHECKPOINT 3 (10s): Assert received retained online ───
        var cp3 = await WaitForMessageAsync(
            m => m.Status == "online" && m.EventType == "device_online",
            TimeSpan.FromSeconds(12),
            "CP3: retained online after restart");

        Assert.NotNull(cp3);
        Assert.Equal("online", cp3!.Status);
        Assert.Equal("device_online", cp3.EventType);
        Assert.True(cp3.Ts > 0, $"Active publish ts should be > 0 after restart, got {cp3.Ts}");

        _output.WriteLine("[CHECKPOINT 3 PASSED] Received online retained message after restart");

        _output.WriteLine("=== All checkpoints passed ===");
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();

        if (_testSubscriber != null)
        {
            try
            {
                if (_testSubscriber.IsConnected)
                    await _testSubscriber.DisconnectAsync(new MqttClientDisconnectOptions());
                _testSubscriber.Dispose();
            }
            catch { }
        }

        if (_webApiProcess != null && !_webApiProcess.HasExited)
        {
            try
            {
                _webApiProcess.Kill(entireProcessTree: true);
                using var waitCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _webApiProcess.WaitForExitAsync(waitCts.Token);
            }
            catch { }
            _webApiProcess.Dispose();
        }

        _cts.Dispose();
        _messageSync.Dispose();

        GC.SuppressFinalize(this);
    }
}
