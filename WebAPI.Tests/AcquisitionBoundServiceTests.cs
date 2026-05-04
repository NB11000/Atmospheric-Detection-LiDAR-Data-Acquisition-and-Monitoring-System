using System;
using System.Threading;
using WebAPI.Service;
using Xunit;

namespace WebAPI.Tests;

public class AcquisitionBoundServiceTests
{
    // Simulates the idempotency pattern used by all three services
    private sealed class TestService : IAcquisitionBoundService, IDisposable
    {
        private readonly object _lock = new();
        private bool _isRunning;
        private CancellationTokenSource? _cts;

        public bool RequiresMqttConnection => false;
        public int StartCallCount { get; private set; }
        public int StopCallCount { get; private set; }
        public bool IsRunning
        {
            get { lock (_lock) return _isRunning; }
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
            StartCallCount++;
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

            StopCallCount++;
        }

        public void Dispose()
        {
            _cts?.Dispose();
        }
    }

    [Fact]
    public void Start_WhenRunning_IsIdempotent()
    {
        using var service = new TestService();

        service.Start();
        Assert.True(service.IsRunning);
        Assert.Equal(1, service.StartCallCount);

        service.Start(); // 重复
        Assert.Equal(1, service.StartCallCount);
    }

    [Fact]
    public void Stop_WhenStopped_IsIdempotent()
    {
        using var service = new TestService();

        service.Stop(); // 未运行，不执行
        Assert.Equal(0, service.StopCallCount);

        service.Start();
        service.Stop();
        Assert.Equal(1, service.StopCallCount);

        service.Stop(); // 重复
        Assert.Equal(1, service.StopCallCount);
    }

    [Fact]
    public void StartAfterStop_Works()
    {
        using var service = new TestService();

        service.Start();
        service.Stop();
        service.Start();

        Assert.True(service.IsRunning);
        Assert.Equal(2, service.StartCallCount);
    }
}
