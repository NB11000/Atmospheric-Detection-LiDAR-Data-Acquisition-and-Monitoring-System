using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ConsoleApp1.Helpers;
using ConsoleApp1.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedModels;
using WebAPISharedMemoryFramework;

namespace WebAPI.Service
{
    /// <summary>
    /// 持久化服务（采集绑定）
    /// </summary>
    public class PersistenceService : IAcquisitionBoundService, IDisposable
    {
        private static readonly string[] CsvHeader = { "Timestamp", "UTC", "CH1", "CH2", "Vis", "Cn2", "Temp", "Humi", "Press", "WindSpd", "Rain", "WindDir" };

        public bool RequiresMqttConnection => false;

        /// <summary>
        /// 持久化服务周期（秒），每隔固定时间从 CoreDataBus 读取最新数据样本，并追加写入当天的 CSV 文件
        /// </summary>
        private const int IntervalSeconds = 5;

        private readonly CoreDataBus _coreDataBus;
        private readonly IOptionsMonitor<PersistenceSettings> _settings;
        private readonly ILogger<PersistenceService> _logger;

        private readonly object _lock = new();
        private bool _isRunning;
        private CancellationTokenSource? _cts;

        public PersistenceService(
            CoreDataBus coreDataBus,
            IOptionsMonitor<PersistenceSettings> settings,
            ILogger<PersistenceService> logger)
        {
            _coreDataBus = coreDataBus;
            _settings = settings;
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

            _logger.LogInformation("持久化服务已启动，周期={Interval}s", IntervalSeconds);
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

            _logger.LogInformation("持久化服务已停止");
        }

        /// <summary>
        /// 周期性从 CoreDataBus 读取最新数据样本，并追加写入当天的 CSV 文件
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        private async Task RunLoopAsync(CancellationToken ct)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(IntervalSeconds));
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

                    var now = DateTime.Now;
                    var fileName = $"{now:yyyy-MM-dd}_{now:HH}.csv";
                    var dataDir = _settings.CurrentValue.DataDirectory;
                    var filePath = Path.Combine(dataDir, fileName);

                    Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

                    bool writeHeader = !File.Exists(filePath);
                    using var fs = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read);
                    using var sw = new StreamWriter(fs);
                    // 如果是新文件，先写入表头
                    if (writeHeader)
                        await sw.WriteLineAsync(string.Join(",", CsvHeader));
                    // 追加写入数据行
                    var line = string.Join(",",
                        sample.Timestamp,
                        utc.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ"),
                        sample.CH1, sample.CH2, sample.Vis, sample.Cn2,
                        sample.Temp, sample.Humi, sample.Press,
                        sample.WindSpd, sample.Rain, sample.WindDir);

                    await sw.WriteLineAsync(line);
                    await sw.FlushAsync();
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "持久化写入失败，下次周期重试");
                }
            }
        }

        public void Dispose()
        {
            _cts?.Dispose();
        }
    }
}
