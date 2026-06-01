using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using AvaloniaApplication_ConfigLauncher;
using AvaloniaApplication_ConfigLauncher.ViewModels;
using Xunit;

namespace AvaloniaApplication_ConfigLauncher.Tests;

public class MainWindowViewModelTests
{
    // ── RED #1 ──────────────────────────────────────────────

    [Fact]
    public async Task ShutdownWebApiAsync_ReturnsTrue_WhenSuccessful()
    {
        var fakeHandler = new FakeHttpMessageHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.OK)
        };
        using var httpClient = new HttpClient(fakeHandler);
        var launcherClient = new LauncherHttpClient("http://localhost:5135", httpClient);

        var fakePm = new FakeProcessManager { StopAsyncResult = true };

        var (cm, tempDir) = CreateTempConfigManager();
        try
        {
            cm.SaveBaseUrl("http://localhost:5135");
            var vm = new MainWindowViewModel(cm, fakePm, launcherClient);

            var result = await vm.ShutdownWebApiAsync();

            Assert.True(result);
            Assert.True(fakePm.StopAsyncCalled);
        }
        finally
        {
            System.IO.Directory.Delete(tempDir, true);
        }
    }

    // ── RED #2 ──────────────────────────────────────────────

    [Fact]
    public async Task ShutdownWebApiAsync_ReturnsFalse_WhenHttpUnreachable()
    {
        var fakeHandler = new FakeHttpMessageHandler
        {
            ThrowException = new HttpRequestException("Connection refused")
        };
        using var httpClient = new HttpClient(fakeHandler);
        var launcherClient = new LauncherHttpClient("http://localhost:5135", httpClient);

        var fakePm = new FakeProcessManager { StopAsyncResult = true };

        var (cm, tempDir) = CreateTempConfigManager();
        try
        {
            cm.SaveBaseUrl("http://localhost:5135");
            var vm = new MainWindowViewModel(cm, fakePm, launcherClient);

            var result = await vm.ShutdownWebApiAsync();

            Assert.False(result);
        }
        finally
        {
            System.IO.Directory.Delete(tempDir, true);
        }
    }

    // ── RED #3 ──────────────────────────────────────────────

    [Fact]
    public async Task ShutdownWebApiAsync_ReturnsFalse_WhenTimeout()
    {
        var fakeHandler = new FakeHttpMessageHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.OK)
        };
        using var httpClient = new HttpClient(fakeHandler);
        var launcherClient = new LauncherHttpClient("http://localhost:5135", httpClient);

        var fakePm = new FakeProcessManager { StopAsyncResult = false };

        var (cm, tempDir) = CreateTempConfigManager();
        try
        {
            cm.SaveBaseUrl("http://localhost:5135");
            var vm = new MainWindowViewModel(cm, fakePm, launcherClient);

            var result = await vm.ShutdownWebApiAsync();

            Assert.False(result);
        }
        finally
        {
            System.IO.Directory.Delete(tempDir, true);
        }
    }

    // ── RED #4 ──────────────────────────────────────────────

    [Fact]
    public async Task IsWebApiHttpReachableAsync_ReturnsTrue_WhenHttp200()
    {
        var fakeHandler = new FakeHttpMessageHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.OK)
        };
        using var reachabilityClient = new HttpClient(fakeHandler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        var launcherClient = new LauncherHttpClient("http://localhost:5135");
        var fakePm = new FakeProcessManager();

        var (cm, tempDir) = CreateTempConfigManager();
        try
        {
            cm.SaveBaseUrl("http://localhost:5135");
            var vm = new MainWindowViewModel(cm, fakePm, launcherClient, reachabilityClient);

            var result = await vm.IsWebApiHttpReachableAsync();

            Assert.True(result);
        }
        finally
        {
            System.IO.Directory.Delete(tempDir, true);
        }
    }

    // ── RED #5 ──────────────────────────────────────────────

    [Fact]
    public async Task IsWebApiHttpReachableAsync_ReturnsFalse_WhenHttpError()
    {
        var fakeHandler = new FakeHttpMessageHandler
        {
            ThrowException = new HttpRequestException("Connection refused")
        };
        using var reachabilityClient = new HttpClient(fakeHandler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        var launcherClient = new LauncherHttpClient("http://localhost:5135");
        var fakePm = new FakeProcessManager();

        var (cm, tempDir) = CreateTempConfigManager();
        try
        {
            cm.SaveBaseUrl("http://localhost:5135");
            var vm = new MainWindowViewModel(cm, fakePm, launcherClient, reachabilityClient);

            var result = await vm.IsWebApiHttpReachableAsync();

            Assert.False(result);
        }
        finally
        {
            System.IO.Directory.Delete(tempDir, true);
        }
    }

    // ── Helpers ─────────────────────────────────────────────

    private static (ConfigManager, string) CreateTempConfigManager()
    {
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString());
        System.IO.Directory.CreateDirectory(tempDir);
        var cm = new ConfigManager(tempDir);
        return (cm, tempDir);
    }

    private class FakeProcessManager : IWebApiProcessManager
    {
        public bool IsProcessRunning { get; set; }
        public bool StopAsyncCalled { get; private set; }
        public bool StopAsyncResult { get; set; } = true;

        public System.Diagnostics.Process Start()
        {
            return new System.Diagnostics.Process();
        }

        public Task<bool> WaitUntilReadyAsync(string baseUrl, int timeoutSec = 30)
        {
            return Task.FromResult(true);
        }

        public Task<bool> StopAsync(string baseUrl, int timeoutSec = 10)
        {
            StopAsyncCalled = true;
            return Task.FromResult(StopAsyncResult);
        }
    }

    private class FakeHttpMessageHandler : HttpMessageHandler
    {
        public HttpResponseMessage? Response { get; set; }
        public HttpRequestMessage? LastRequest { get; private set; }
        public System.Exception? ThrowException { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (ThrowException != null)
                return Task.FromException<HttpResponseMessage>(ThrowException);
            return Task.FromResult(Response ?? new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
