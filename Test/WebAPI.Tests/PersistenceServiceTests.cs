using System;
using System.IO;
using System.Threading.Tasks;
using ConsoleApp1.Helpers;
using ConsoleApp1.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharedModels;
using WebAPI.Service;
using WebAPISharedMemoryFramework;
using Xunit;

namespace WebAPI.Tests;

public class PersistenceServiceTests : IDisposable
{
    private readonly string _tempDir;

    public PersistenceServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"PersistenceTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // ── Test 1: 基础写入 + 多周期追加 + Stop 后停止 ────────

    [Fact]
    public async Task WriteAndAppendAndStop()
    {
        var busName = $"TEST_BUS_1_{Guid.NewGuid():N}";
        var bus = new CoreDataBus(busName);
        try
        {
            bus.Create(channels: 2, buffer: 100, sampleRate: 1_000_000);

            var svc = CreateService(bus);

            // Round 1: write one sample, start service, verify CSV
            var s1 = Sample(ts: 1, ch1: 1.1, ch2: 2.1, vis: 3.1);
            bus.Write(ref s1);

            svc.Start();
            await Task.Delay(6500); // 5s cycle + margin

            var files1 = Directory.GetFiles(_tempDir, "*.csv");
            Assert.Single(files1);
            var lines1 = File.ReadAllLines(files1[0]);
            Assert.Equal(2, lines1.Length); // header + 1 data line
            AssertCsvHeader(lines1[0]);
            AssertCsvLine(lines1[1], ts: 1, ch1: 1.1, ch2: 2.1, vis: 3.1);

            // Round 2: write another sample, wait, verify append
            var s2 = Sample(ts: 2, ch1: 4.1, ch2: 5.1, vis: 6.1);
            bus.Write(ref s2);

            await Task.Delay(6500);

            var lines2 = File.ReadAllLines(files1[0]);
            Assert.Equal(3, lines2.Length); // header + 2 data lines
            AssertCsvLine(lines2[2], ts: 2, ch1: 4.1, ch2: 5.1, vis: 6.1);

            // Round 3: Stop service, write, wait, verify NO new line
            svc.Stop();
            var s3 = Sample(ts: 3, ch1: 7.1, ch2: 8.1, vis: 9.1);
            bus.Write(ref s3);

            await Task.Delay(6500);

            var lines3 = File.ReadAllLines(files1[0]);
            Assert.Equal(3, lines3.Length); // still 3, no append after stop
        }
        finally
        {
            bus.Dispose();
        }
    }

    // ── Test 2: 空总线不创建文件 ───────────────────────────

    [Fact]
    public async Task EmptyBus_NoFileCreated()
    {
        var busName = $"TEST_BUS_2_{Guid.NewGuid():N}";
        var bus = new CoreDataBus(busName);
        try
        {
            bus.Create(channels: 2, buffer: 100, sampleRate: 1_000_000);
            var svc = CreateService(bus);

            svc.Start();
            await Task.Delay(6500);
            svc.Stop();

            var csvFiles = Directory.GetFiles(_tempDir, "*.csv");
            Assert.Empty(csvFiles);
        }
        finally
        {
            bus.Dispose();
        }
    }

    // ── Test 3: UTC 时间还原正确性 ──────────────────────────

    [Fact]
    public async Task UtcConversion_Correct()
    {
        var busName = $"TEST_BUS_3_{Guid.NewGuid():N}";
        var bus = new CoreDataBus(busName);
        try
        {
            bus.Create(channels: 2, buffer: 100, sampleRate: 1_000_000);

            // Capture known reference values
            var refTick = bus.ReferenceTick;
            var refUtcTicks = bus.ReferenceUtcTicks;

            var svc = CreateService(bus);

            var s = Sample(ts: 42, ch1: 9.9);
            bus.Write(ref s);

            svc.Start();
            await Task.Delay(6500);

            var files = Directory.GetFiles(_tempDir, "*.csv");
            Assert.Single(files);
            var lines = File.ReadAllLines(files[0]);
            Assert.Equal(2, lines.Length);

            var expectedUtc = TimeHelper.ToUtcDateTime(s.Time, refTick, refUtcTicks, System.Diagnostics.Stopwatch.Frequency);
            var expectedUtcStr = expectedUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ");

            var fields = lines[1].Split(',');
            Assert.Equal(expectedUtcStr, fields[1]);
        }
        finally
        {
            bus.Dispose();
        }
    }

    // ── Helpers ─────────────────────────────────────────────

    private PersistenceService CreateService(CoreDataBus bus)
    {
        var settings = new PersistenceSettings { DataDirectory = _tempDir };
        var options = new TestOptions<PersistenceSettings>(settings);
        var logger = NullLogger<PersistenceService>.Instance;
        return new PersistenceService(bus, options, logger);
    }

    private static StructuredSample Sample(long ts, double ch1, double ch2 = 0, double vis = 0)
    {
        return new StructuredSample
        {
            Timestamp = ts,
            Time = System.Diagnostics.Stopwatch.GetTimestamp(),
            CH1 = ch1,
            CH2 = ch2,
            Vis = vis,
            Cn2 = -1.0,
            Temp = 25.0,
            Humi = 60.0,
            Press = 1013.0,
            WindSpd = 5.0,
            Rain = 0.0,
            WindDir = 180.0
        };
    }

    private static void AssertCsvHeader(string line)
    {
        Assert.StartsWith("Timestamp,UTC,CH1,CH2,Vis,Cn2", line);
    }

    private static void AssertCsvLine(string line, long ts, double ch1, double ch2, double vis)
    {
        var fields = line.Split(',');
        Assert.Equal(ts.ToString(), fields[0]);
        Assert.Equal(ch1.ToString("G"), fields[2]);
        Assert.Equal(ch2.ToString("G"), fields[3]);
        Assert.Equal(vis.ToString("G"), fields[4]);
    }

    private class TestOptions<T> : IOptionsMonitor<T>
    {
        public T CurrentValue { get; }
        public TestOptions(T value) => CurrentValue = value;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
