using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ConsoleApp1.Helpers;
using ConsoleApp1.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebAPI.Models;
using SharedModels;
using WebAPISharedMemoryFramework;

namespace WebAPI.Service
{
    /// <summary>
    /// 低频发布服务（采集绑定）
    /// </summary>
    public class LowFrequencyPublisher : IAcquisitionBoundService, IDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public bool RequiresMqttConnection => true;

        private readonly CoreDataBus _coreDataBus;
        private readonly MqttEventPublisher _mqttEventPublisher;
        private readonly IOptionsMonitor<MqttSettings> _mqttSettings;
        private readonly ILogger<LowFrequencyPublisher> _logger;

        private readonly object _lock = new();
        private bool _isRunning;
        private CancellationTokenSource? _cts;

        public LowFrequencyPublisher(
            CoreDataBus coreDataBus,
            MqttEventPublisher mqttEventPublisher,
            IOptionsMonitor<MqttSettings> mqttSettings,
            ILogger<LowFrequencyPublisher> logger)
        {
            _coreDataBus = coreDataBus;
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

            _logger.LogInformation("低频发布服务已启动");
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

            _logger.LogInformation("低频发布服务已停止");
        }

        private async Task RunLoopAsync(CancellationToken ct)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(7));
            while (await timer.WaitForNextTickAsync(ct))
            {
                try
                {
                    if (!_coreDataBus.TryReadLatestSingle(out var sample))
                        continue;

                    var utc = TimeHelper.ToUtcDateTime(
                        sample.Time,
                        _coreDataBus.ReferenceTick,
                        _coreDataBus.ReferenceUtcTicks,
                        Stopwatch.Frequency);

                    var payload = new
                    {
                        Timestamp = sample.Timestamp,
                        UTC = utc.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ"),
                        sample.CH1,
                        sample.CH2,
                        sample.Vis,
                        sample.Cn2,
                        sample.Temp,
                        sample.Humi,
                        sample.Press,
                        sample.WindSpd,
                        sample.Rain,
                        sample.WindDir
                    };

                    var json = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
                    var topic = $"daq/{_mqttSettings.CurrentValue.MachineId}/lowfreq";

                    var client = _mqttEventPublisher.MqttClient;
                    if (client != null && client.IsConnected)
                    {
                        await client.PublishAsync(new MQTTnet.MqttApplicationMessageBuilder()
                            .WithTopic(topic)
                            .WithPayload(json)
                            .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                            .Build());
                    }
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "低频发布失败，下次周期重试");
                }
            }
        }

        public void Dispose()
        {
            _cts?.Dispose();
        }
    }
}
