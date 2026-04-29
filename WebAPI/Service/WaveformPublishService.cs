using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebAPI.Models;
using WebAPISharedMemoryFramework;

namespace WebAPI.Service
{
    /// <summary>
    /// 波形发布后台服务（定时轮询型）
    /// 由 SystemStateService.AcquiringStateChanged 事件驱动启停
    /// 每帧从共享内存读取双通道波形 → 二进制发布到 MQTT
    /// </summary>
    public class WaveformPublishService : BackgroundService
    {
        /// <summary>
        /// 每帧采样点数（与共享内存帧大小一致）
        /// </summary>
        private const int WaveformFramePoints = 1000;

        /// <summary>
        /// 单通道每帧字节数（1000 × sizeof(double) = 8000）
        /// </summary>
        private const int WaveformFrameBytes = WaveformFramePoints * sizeof(double);

        private readonly MqttEventPublisher _mqttEventPublisher;
        private readonly SystemStateService _systemStateService;
        private readonly UISharedBuffer _uiSharedBuffer;
        private readonly IOptionsMonitor<MqttSettings> _mqttSettings;
        private readonly ILogger<WaveformPublishService> _logger;

        /// <summary>
        /// 波形读取缓冲区（双通道 1000×double）
        /// </summary>
        private double[] _waveformBuf1;
        private double[] _waveformBuf2;

        /// <summary>
        /// 波形发布复用字节缓冲区（从 ArrayPool 租用，Dispose 时归还）
        /// </summary>
        private readonly byte[] _ch1Bytes;
        private readonly byte[] _ch2Bytes;

        /// <summary>
        /// 启停同步锁
        /// </summary>
        private readonly object _waveformLock = new();

        /// <summary>
        /// 循环是否正在运行
        /// </summary>
        private bool _isRunning;

        /// <summary>
        /// 波形发布循环取消令牌源
        /// </summary>
        private CancellationTokenSource? _waveformCts;

        public WaveformPublishService(
            MqttEventPublisher mqttEventPublisher,
            SystemStateService systemStateService,
            UISharedBuffer uiSharedBuffer,
            IOptionsMonitor<MqttSettings> mqttSettings,
            ILogger<WaveformPublishService> logger)
        {
            _mqttEventPublisher = mqttEventPublisher;
            _systemStateService = systemStateService;
            _uiSharedBuffer = uiSharedBuffer;
            _mqttSettings = mqttSettings;
            _logger = logger;

            // 预分配波形读取缓冲区
            _waveformBuf1 = new double[WaveformFramePoints];
            _waveformBuf2 = new double[WaveformFramePoints];

            // 从 ArrayPool 租用字节缓冲区，循环内复用
            _ch1Bytes = new byte[WaveformFrameBytes];
            _ch2Bytes = new byte[WaveformFrameBytes];

            // 订阅采集状态变更事件，作为波形发布启停的唯一触发源
            systemStateService.AcquiringStateChanged += OnAcquiringStateChanged;
        }

        /// <summary>
        /// BackgroundService 入口（空实现，由事件驱动）
        /// </summary>
        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// 采集状态变更回调
        /// </summary>
        private void OnAcquiringStateChanged(bool isAcquiring)
        {
            if (isAcquiring)
                Start();
            else
                Stop();
        }

        /// <summary>
        /// 线程安全的启动方法（幂等：已在运行时直接返回）
        /// </summary>
        public void Start()
        {
            lock (_waveformLock)
            {
                if (_isRunning)
                {
                    _logger.LogDebug("波形发布循环已在运行，忽略重复启动");
                    return;
                }

                _isRunning = true;
            }

            _waveformCts?.Cancel();
            _waveformCts?.Dispose();
            _waveformCts = new CancellationTokenSource();
            _ = RunLoopAsync(_waveformCts.Token);

            _logger.LogInformation("波形发布循环已启动");
        }

        /// <summary>
        /// 线程安全的停止方法（幂等：已停止时直接返回）
        /// </summary>
        public void Stop()
        {
            CancellationTokenSource? ctsToCancel = null;

            lock (_waveformLock)
            {
                if (!_isRunning)
                {
                    return;
                }

                _isRunning = false;
                ctsToCancel = _waveformCts;
            }

            try
            {
                ctsToCancel?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // 可能已被 Dispose，忽略
            }

            _logger.LogInformation("波形发布循环已停止");
        }

        /// <summary>
        /// 波形数据发布循环：定时从共享内存读取 → BlockCopy 到字节缓冲区 → 发布到 MQTT
        /// </summary>
        private async Task RunLoopAsync(CancellationToken ct)
        {
            var interval = _mqttSettings.CurrentValue.WaveformPublishIntervalMs;

            // 使用 PeriodicTimer 实现定时循环，支持异步等待和取消
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(interval));
            // 循环直到取消请求，异常由调用者捕获并记录
            while (await timer.WaitForNextTickAsync(ct))
            {
                try
                {
                    // 从共享内存读取最新双通道波形帧
                    _uiSharedBuffer.ReadLatestFrame(ref _waveformBuf1, ref _waveformBuf2);

                    // 将 double[] 拷贝到字节缓冲区（避免每帧分配）
                    Buffer.BlockCopy(_waveformBuf1, 0, _ch1Bytes, 0, WaveformFrameBytes);
                    Buffer.BlockCopy(_waveformBuf2, 0, _ch2Bytes, 0, WaveformFrameBytes);

                    // 调用统一的 MQTT 发布方法（连接检查由内部处理）
                    await _mqttEventPublisher.PublishWaveformDataAsync(
                        _ch1Bytes, _ch2Bytes, WaveformFrameBytes);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "波形发布失败，下次循环重试");
                }
            }
        }

        /// <summary>
        /// 服务销毁：归还 ArrayPool 缓冲区并释放 CTS
        /// </summary>
        public override void Dispose()
        {

            _waveformCts?.Dispose();

            base.Dispose();
        }
    }
}
