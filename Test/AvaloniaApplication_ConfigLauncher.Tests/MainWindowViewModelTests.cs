using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Media;
using AvaloniaApplication_ConfigLauncher;
using AvaloniaApplication_ConfigLauncher.ViewModels;
using SharedModels;
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
    public async Task RefreshConnectionStatus_HttpReachable_SetsWebApiConnected()
    {
        var reachableHandler = new FakeHttpMessageHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.OK)
        };
        using var reachableClient = new HttpClient(reachableHandler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

        var systemStateJson = JsonSerializer.Serialize(new SystemStateDto
        {
            Server = new ServerStateDto { IsApiAlive = true, IsMqttConnected = true }
        });
        var systemHandler = new FakeHttpMessageHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(systemStateJson, Encoding.UTF8, "application/json")
            }
        };
        using var systemClient = new HttpClient(systemHandler);
        var launcherClient = new LauncherHttpClient("http://localhost:5135", systemClient);
        var fakePm = new FakeProcessManager();

        var (cm, tempDir) = CreateTempConfigManager();
        try
        {
            cm.SaveBaseUrl("http://localhost:5135");
            var vm = new MainWindowViewModel(cm, fakePm, launcherClient, reachableClient);

            await vm.RefreshConnectionStatusAsync();

            Assert.Equal("已连接", vm.WebApiConnectionStatus);
            Assert.Equal(Brushes.Green, vm.WebApiConnectionColor);
            Assert.Equal("已连接", vm.MqttConnectionStatus);
            Assert.Equal(Brushes.Green, vm.MqttConnectionColor);
        }
        finally
        {
            System.IO.Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task RefreshConnectionStatus_HttpUnreachable_SetsWebApiDisconnected_MqttUnknown()
    {
        var reachableHandler = new FakeHttpMessageHandler
        {
            ThrowException = new HttpRequestException("Connection refused")
        };
        using var reachableClient = new HttpClient(reachableHandler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

        var systemHandler = new FakeHttpMessageHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            }
        };
        using var systemClient = new HttpClient(systemHandler);
        var launcherClient = new LauncherHttpClient("http://localhost:5135", systemClient);
        var fakePm = new FakeProcessManager();

        var (cm, tempDir) = CreateTempConfigManager();
        try
        {
            cm.SaveBaseUrl("http://localhost:5135");
            var vm = new MainWindowViewModel(cm, fakePm, launcherClient, reachableClient);

            await vm.RefreshConnectionStatusAsync();

            Assert.Equal("未连接", vm.WebApiConnectionStatus);
            Assert.Equal(Brushes.Gray, vm.WebApiConnectionColor);
            Assert.Equal("—", vm.MqttConnectionStatus);
            Assert.Equal(Brushes.Gold, vm.MqttConnectionColor);
        }
        finally
        {
            System.IO.Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task RefreshConnectionStatus_MqttDisconnected_ShowsGray()
    {
        var reachableHandler = new FakeHttpMessageHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.OK)
        };
        using var reachableClient = new HttpClient(reachableHandler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

        var systemStateJson = JsonSerializer.Serialize(new SystemStateDto
        {
            Server = new ServerStateDto { IsApiAlive = true, IsMqttConnected = false }
        });
        var systemHandler = new FakeHttpMessageHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(systemStateJson, Encoding.UTF8, "application/json")
            }
        };
        using var systemClient = new HttpClient(systemHandler);
        var launcherClient = new LauncherHttpClient("http://localhost:5135", systemClient);
        var fakePm = new FakeProcessManager();

        var (cm, tempDir) = CreateTempConfigManager();
        try
        {
            cm.SaveBaseUrl("http://localhost:5135");
            var vm = new MainWindowViewModel(cm, fakePm, launcherClient, reachableClient);

            await vm.RefreshConnectionStatusAsync();

            Assert.Equal("已连接", vm.WebApiConnectionStatus);
            Assert.Equal(Brushes.Green, vm.WebApiConnectionColor);
            Assert.Equal("未连接", vm.MqttConnectionStatus);
            Assert.Equal(Brushes.Gray, vm.MqttConnectionColor);
        }
        finally
        {
            System.IO.Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ConfigVm_NotifyBaseUrlChanged_IsWired()
    {
        var fakePm = new FakeProcessManager();
        var launcherClient = new LauncherHttpClient("http://localhost:5135");

        var (cm, tempDir) = CreateTempConfigManager();
        try
        {
            cm.SaveBaseUrl("http://localhost:5135");
            var vm = new MainWindowViewModel(cm, fakePm, launcherClient);

            Assert.NotNull(vm.ConfigVm.NotifyBaseUrlChanged);
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
