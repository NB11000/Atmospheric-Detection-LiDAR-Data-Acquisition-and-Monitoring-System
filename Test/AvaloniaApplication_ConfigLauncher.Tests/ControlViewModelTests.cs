using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaApplication_ConfigLauncher;
using AvaloniaApplication_ConfigLauncher.ViewModels;
using SharedModels;
using Xunit;

namespace AvaloniaApplication_ConfigLauncher.Tests;

public class ControlViewModelTests
{
    private class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static StringContent JsonContent(object obj)
    {
        var json = JsonSerializer.Serialize(obj, _jsonOptions);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private static (LauncherHttpClient, ControlViewModel) CreateVm(
        Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var httpClient = new HttpClient(new FakeHttpMessageHandler(handler))
        {
            BaseAddress = new Uri("http://localhost:5135")
        };
        var launcher = new LauncherHttpClient("http://localhost:5135", httpClient);
        var vm = new ControlViewModel(launcher);
        return (launcher, vm);
    }

    private static CommandResult OkResult(string code, string message)
    {
        return new CommandResult { Success = true, Code = code, Message = message };
    }

    [Fact]
    public async Task OpenDeviceCommand_Updates_OpenDeviceResult()
    {
        var (_, vm) = CreateVm(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent(OkResult("COLLECTOR_OPENED", "采集卡已打开"))
            });

        vm.OpenDeviceCommand.Execute(null);
        await Task.Delay(100);

        Assert.NotNull(vm.OpenDeviceResult);
        Assert.True(vm.OpenDeviceResult!.Success);
        Assert.Equal("COLLECTOR_OPENED", vm.OpenDeviceResult.Code);
    }

    [Fact]
    public async Task CloseDeviceCommand_Updates_CloseDeviceResult()
    {
        var (_, vm) = CreateVm(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent(OkResult("COLLECTOR_CLOSED", "采集卡已关闭"))
            });

        vm.CloseDeviceCommand.Execute(null);
        await Task.Delay(100);

        Assert.NotNull(vm.CloseDeviceResult);
        Assert.True(vm.CloseDeviceResult!.Success);
    }

    [Fact]
    public async Task StartAcquisitionCommand_Updates_StartAcqResult()
    {
        var (_, vm) = CreateVm(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent(OkResult("AD_STARTED", "采集已开始"))
            });

        vm.StartAcquisitionCommand.Execute(null);
        await Task.Delay(100);

        Assert.NotNull(vm.StartAcqResult);
        Assert.True(vm.StartAcqResult!.Success);
    }

    [Fact]
    public async Task StopAcquisitionCommand_Updates_StopAcqResult()
    {
        var (_, vm) = CreateVm(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent(OkResult("AD_STOPPED", "采集已停止"))
            });

        vm.StopAcquisitionCommand.Execute(null);
        await Task.Delay(100);

        Assert.NotNull(vm.StopAcqResult);
        Assert.True(vm.StopAcqResult!.Success);
    }

    [Fact]
    public async Task LaserConnectCommand_Updates_LaserConnectResult()
    {
        var (_, vm) = CreateVm(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent(OkResult("LASER_CONNECTED", "激光器已连接"))
            });

        vm.LaserConnectCommand.Execute(null);
        await Task.Delay(100);

        Assert.NotNull(vm.LaserConnectResult);
        Assert.True(vm.LaserConnectResult!.Success);
    }

    [Fact]
    public async Task LaserOnCommand_Updates_LaserOnResult()
    {
        var (_, vm) = CreateVm(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent(OkResult("LASER_ON", "激光已开启"))
            });

        vm.LaserOnCommand.Execute(null);
        await Task.Delay(100);

        Assert.NotNull(vm.LaserOnResult);
        Assert.True(vm.LaserOnResult!.Success);
    }

    [Fact]
    public async Task RefreshStateCommand_Updates_CurrentState()
    {
        var state = new SystemStateDto
        {
            Server = new ServerStateDto { IsApiAlive = true },
            Collector = new CollectorStateDto
            {
                ProcessConnected = true,
                DeviceOpened = false,
                Acquiring = false
            },
            Laser = new LaserStateDto
            {
                SerialConnected = true,
                EmissionOn = true,
                PortName = "COM3"
            },
            Timestamp = DateTime.Now
        };
        var (_, vm) = CreateVm(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent(state) });

        Assert.False(vm.IsRefreshing);

        vm.RefreshStateCommand.Execute(null);
        await Task.Delay(100);

        Assert.NotNull(vm.CurrentState);
        Assert.True(vm.CurrentState!.Server.IsApiAlive);
        Assert.True(vm.CurrentState.Collector.ProcessConnected);
        Assert.True(vm.CurrentState.Laser.EmissionOn);
        Assert.Equal("COM3", vm.CurrentState.Laser.PortName);
        Assert.False(vm.IsRefreshing);
    }

    [Fact]
    public async Task Command_WithoutHttpClient_ReturnsError()
    {
        var vm = new ControlViewModel(null);

        vm.OpenDeviceCommand.Execute(null);
        await Task.Delay(100);

        Assert.NotNull(vm.OpenDeviceResult);
        Assert.False(vm.OpenDeviceResult!.Success);
        Assert.Equal("CONNECTION_ERROR", vm.OpenDeviceResult.Code);
        Assert.Contains("无法连接到 WebAPI", vm.OpenDeviceResult.Message);
    }

    [Fact]
    public void DefaultConstructor_Creates_WithNullHttpClient()
    {
        var vm = new ControlViewModel();

        Assert.Equal("WebAPI 未启动", vm.PlaceholderText);
        Assert.Null(vm.CurrentState);
    }

    [Fact]
    public async Task RefreshState_WithoutHttpClient_SetsNull()
    {
        var vm = new ControlViewModel(null);
        vm.CurrentState = new SystemStateDto();

        vm.RefreshStateCommand.Execute(null);
        await Task.Delay(100);

        Assert.Null(vm.CurrentState);
    }

    [Fact]
    public async Task LaserDisconnectCommand_Updates_LaserDisconnectResult()
    {
        var (_, vm) = CreateVm(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent(OkResult("LASER_DISCONNECTED", "激光器已断开"))
            });

        vm.LaserDisconnectCommand.Execute(null);
        await Task.Delay(100);

        Assert.NotNull(vm.LaserDisconnectResult);
        Assert.True(vm.LaserDisconnectResult!.Success);
    }

    [Fact]
    public async Task LaserOffCommand_Updates_LaserOffResult()
    {
        var (_, vm) = CreateVm(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent(OkResult("LASER_OFF", "激光已关闭"))
            });

        vm.LaserOffCommand.Execute(null);
        await Task.Delay(100);

        Assert.NotNull(vm.LaserOffResult);
        Assert.True(vm.LaserOffResult!.Success);
    }
}
