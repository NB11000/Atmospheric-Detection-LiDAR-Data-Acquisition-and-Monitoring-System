using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebAPI.Models;
using WebAPISharedMemoryFramework;

namespace WebAPI.Service
{
    /// <summary>
    /// 波形发布服务（采集绑定）
    /// 由 AcquisitionLifecycleCoordinator 通过 Start/Stop 驱动启停
    /// 每帧从共享内存读取双通道波形 → 二进制发布到 MQTT
    /// </summary>
    public class WaveformPublishService : IAcquisitionBoundService, IDisposable
    {
        private const int WaveformFramePoints = 1000;
        private const int WaveformFrameBytes = WaveformFramePoints * sizeof(double);

        public bool RequiresMqttConnection => true;

        private readonly MqttEventPublisher _mqttEventPublisher;
        private readonly UISharedBuffer _uiSharedBuffer;
        private readonly IOptionsMonitor<MqttSettings> _mqttSettings;
        private readonly ILogger<WaveformPublishService> _logger;

        private double[] _waveformBuf1;
        private double[] _waveformBuf2;
        private readonly byte[] _ch1Bytes;
        private readonly byte[] _ch2Bytes;

        private readonly object _lock = new();
        private bool _isRunning;
        private CancellationTokenSource? _cts;

        public WaveformPublishService(
            MqttEventPublisher mqttEventPublisher,
            UISharedBuffer uiSharedBuffer,
            IOptionsMonitor<MqttSettings> mqttSettings,
            ILogger<WaveformPublishService> logger)
        {
            _mqttEventPublisher = mqttEventPublisher;
            _uiSharedBuffer = uiSharedBuffer;
            _mqttSettings = mqttSettings;
            _logger = logger;

            _waveformBuf1 = new double[WaveformFramePoints];
            _waveformBuf2 = new double[WaveformFramePoints];
            _ch1Bytes = new byte[WaveformFrameBytes];
            _ch2Bytes = new byte[WaveformFrameBytes];
        }

        public void Start()
        {
            lock (_lock)
            {
                if (_isRunning)
                {
                    _logger.LogDebug("波形发布循环已在运行，忽略重复启动");
                    return;
                }
                _isRunning = true;
            }

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            _ = RunLoopAsync(_cts.Token);

            _logger.LogInformation("波形发布循环已启动");
        }

        public void Stop()
        {
            CancellationTokenSource? ctsToCancel = null;

            lock (_lock)
            {
                if (!_isRunning)
                    return;

                _isRunning = false;
                ctsToCancel = _cts;
            }

            try
            {
                ctsToCancel?.Cancel();
            }
            catch (ObjectDisposedException) { }

            _logger.LogInformation("波形发布循环已停止");
        }

        private async Task RunLoopAsync(CancellationToken ct)
        {
            var interval = _mqttSettings.CurrentValue.WaveformPublishIntervalMs;
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(interval));

            while (await timer.WaitForNextTickAsync(ct))
            {
                try
                {
                    _uiSharedBuffer.ReadLatestFrame(ref _waveformBuf1, ref _waveformBuf2);
                    Buffer.BlockCopy(_waveformBuf1, 0, _ch1Bytes, 0, WaveformFrameBytes);
                    Buffer.BlockCopy(_waveformBuf2, 0, _ch2Bytes, 0, WaveformFrameBytes);
                    await _mqttEventPublisher.PublishWaveformDataAsync(_ch1Bytes, _ch2Bytes, WaveformFrameBytes);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "波形发布失败，下次循环重试");
                }
            }
        }

        public void Dispose()
        {
            _cts?.Dispose();
        }
    }
}
