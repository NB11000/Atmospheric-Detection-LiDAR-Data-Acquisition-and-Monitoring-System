using System.Diagnostics;
using ConsoleApp1.Models;
using SharedMemoryFramework;
using Xunit;

namespace WebAPI.Tests;

public class CrossProcessCoreDataBusTests : IDisposable
{
    private readonly string _mapName;
    private CoreDataBus? _bus;

    public CrossProcessCoreDataBusTests()
    {
        _mapName = $"TEST_CROSSPROC_{Guid.NewGuid():N}";
    }

    public void Dispose()
    {
        _bus?.Dispose();
    }

    private static string MMFWriterExePath =>
        Path.Combine(
            Path.GetDirectoryName(typeof(CrossProcessCoreDataBusTests).Assembly.Location)!,
            "MMFWriter.exe");

    /// <summary>
    /// 启动 MMFWriter 子进程并等待退出，返回 exit code
    /// </summary>
    private static int RunMMFWriter(string args)
    {
        Assert.True(File.Exists(MMFWriterExePath), $"MMFWriter.exe not found at {MMFWriterExePath}");

        var psi = new ProcessStartInfo(MMFWriterExePath, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        using var proc = Process.Start(psi)!;
        proc.WaitForExit(10_000);

        if (proc.ExitCode != 0)
        {
            var stderr = proc.StandardError.ReadToEnd();
            Assert.Fail($"MMFWriter failed with exit code {proc.ExitCode}: {stderr}");
        }
        return proc.ExitCode;
    }

    [Fact]
    public void Create_SubprocessWritesOne_ReadBackSameData()
    {
        _bus = new CoreDataBus(_mapName);
        _bus.Create(channels: 2, buffer: 100, sampleRate: 1_000_000);

        RunMMFWriter($"\"{_mapName}\" 1 3.14 42.0");

        Assert.True(_bus.TryReadLatestSingle(out var sample));
        Assert.Equal(0, sample.Timestamp);
        Assert.Equal(3.14, sample.CH1);
        Assert.Equal(42.0, sample.CH2);
    }

    [Fact]
    public void SubprocessWritesFive_TryReadReturnsLatest()
    {
        _bus = new CoreDataBus(_mapName);
        _bus.Create(channels: 2, buffer: 100, sampleRate: 1_000_000);

        RunMMFWriter($"\"{_mapName}\" 5 1.0 10.0 2.0 20.0 3.0 30.0 4.0 40.0 5.0 50.0");

        Assert.True(_bus.TryReadLatestSingle(out var sample));
        Assert.Equal(4, sample.Timestamp);       // 0-indexed, 第 5 条序号 = 4
        Assert.Equal(5.0, sample.CH1);
        Assert.Equal(50.0, sample.CH2);
    }

    [Fact]
    public void WriteIndex_VisibleAcrossProcesses()
    {
        var count = 3;
        _bus = new CoreDataBus(_mapName);
        _bus.Create(channels: 2, buffer: 100, sampleRate: 1_000_000);

        Assert.Equal(0, _bus.WriteIndex);

        RunMMFWriter($"\"{_mapName}\" {count} 1.0 2.0 3.0 4.0 5.0 6.0");

        Assert.Equal(count, _bus.WriteIndex);
    }

    [Fact]
    public void SubprocessOpen_DoesNotCrash()
    {
        _bus = new CoreDataBus(_mapName);
        _bus.Create(channels: 2, buffer: 100, sampleRate: 1_000_000);

        // 不写入任何数据，仅验证 Open 不崩溃
        RunMMFWriter($"\"{_mapName}\" 0");

        Assert.Equal(0, _bus.WriteIndex);
    }
}
