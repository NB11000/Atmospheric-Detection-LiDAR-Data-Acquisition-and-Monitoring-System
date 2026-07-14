using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ConsoleApp1.Models;
using Microsoft.Extensions.Logging;
using WebAPISharedMemoryFramework;

namespace WebAPI.Tools
{
    public class MockDataWriter
    {
        private readonly CoreDataBus _bus;
        private readonly UISharedBuffer _ui;
        private readonly ILogger _logger;

        public MockDataWriter(CoreDataBus bus, UISharedBuffer ui, ILogger logger)
        {
            _bus = bus;
            _ui = ui;
            _logger = logger;
        }

        public async Task RunLoopAsync(CancellationToken ct)
        {
            long tickPerSample = Stopwatch.Frequency / 1_000_000;
            double[] ch1 = new double[1000];
            double[] ch2 = new double[1000];
            long index = 0;

            _logger.LogInformation("Mock 数据写入器已启动");

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    long baseTick = Stopwatch.GetTimestamp();

                    for (int i = 0; i < 1000; i++)
                    {
                        var s = new StructuredSample
                        {
                            Timestamp = index,
                            Time = baseTick + i * tickPerSample,
                            CH1 = Math.Sin(i * 0.1) * 0.5,
                            CH2 = Math.Cos(i * 0.1) * 0.5,
                            Vis = 10.0 + Math.Sin(index * 0.0001) * 2.0,
                            Cn2 = 1e-14 + (index > 99 ? 1e-16 : -1.0),
                            Temp = 25.0,
                            Humi = 60.0,
                            Press = 1013.0,
                            WindSpd = 3.0,
                            Rain = 0.0,
                            WindDir = 180.0
                        };
                        _bus.Write(ref s);
                        ch1[i] = s.CH1;
                        ch2[i] = s.CH2;
                        index++;
                    }

                    _ui.WriteSampleBatch(ch1, ch2, 1000);
                    await Task.Delay(1, ct);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                _logger.LogInformation("Mock 数据写入器已停止，共写入 {Count} 采样点", index);
            }
        }
    }
}
