using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Protocol;
using WebAPI.Models;
using SharedModels;

namespace WebAPI.Service
{
    /// <summary>
    /// 检测发布服务（采集绑定）
    /// 接收 Detection 线程的结构化告警，发布到 MQTT daq/{id}/detection/alerts
    /// </summary>
    public class DetectionPublisherService : IAcquisitionBoundService, IDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public bool RequiresMqttConnection => true;

        private readonly MqttEventPublisher _mqttEventPublisher;
        private readonly IOptionsMonitor<MqttSettings> _mqttSettings;
        private readonly ILogger<DetectionPublisherService> _logger;

        private readonly Channel<DetectionAlertDto> _channel = Channel.CreateUnbounded<DetectionAlertDto>();
        private readonly object _lock = new();
        private bool _isRunning;
        private CancellationTokenSource? _cts;

        public DetectionPublisherService(
            MqttEventPublisher mqttEventPublisher,
            IOptionsMonitor<MqttSettings> mqttSettings,
            ILogger<DetectionPublisherService> logger)
        {
            _mqttEventPublisher = mqttEventPublisher;
            _mqttSettings = mqttSettings;
            _logger = logger;
        }

        public void Start()
        {
            lock (_lock)
            {
                if (_isRunning) return;
                _isRunning = true;
            }

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            _ = RunLoopAsync(_cts.Token);

            _logger.LogInformation("检测发布服务已启动");
        }

        public void Stop()
        {
            CancellationTokenSource? ctsToCancel = null;

            lock (_lock)
            {
                if (!_isRunning) return;
                _isRunning = false;
                ctsToCancel = _cts;
            }

            try { ctsToCancel?.Cancel(); }
            catch (ObjectDisposedException) { }

            _logger.LogInformation("检测发布服务已停止");
        }

        public void OnAlertReceived(DetectionAlertDto alert)
        {
            _channel.Writer.TryWrite(alert);
        }

        private async Task RunLoopAsync(CancellationToken ct)
        {
            try
            {
                await foreach (var alert in _channel.Reader.ReadAllAsync(ct))
                {
                    try
                    {
                        var payload = JsonSerializer.SerializeToUtf8Bytes(alert, JsonOptions);
                        var topic = $"daq/{_mqttSettings.CurrentValue.MachineId}/detection/alerts";

                        var client = _mqttEventPublisher.MqttClient;
                        if (client != null && client.IsConnected)
                        {
                            await client.PublishAsync(new MqttApplicationMessageBuilder()
                                .WithTopic(topic)
                                .WithPayload(payload)
                                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                                .Build());
                        }
                    }
                    catch (Exception ex) when (!ct.IsCancellationRequested)
                    {
                        _logger.LogWarning(ex, "检测告警发布失败");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // graceful shutdown
            }
        }

        public void Dispose()
        {
            _cts?.Dispose();
        }
    }
}
