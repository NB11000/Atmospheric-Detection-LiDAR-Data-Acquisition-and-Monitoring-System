using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaApplication_ConfigLauncher;
using SharedModels;
using Xunit;

namespace AvaloniaApplication_ConfigLauncher.Tests;

public class LauncherHttpClientTests
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

    private static HttpClient CreateHttpClient(
        Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        return new HttpClient(new FakeHttpMessageHandler(handler))
        {
            BaseAddress = new Uri("http://localhost:5135")
        };
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

    [Fact]
    public async Task OpenDevice_Success_ReturnsCommandResult()
    {
        var expected = new CommandResult
        {
            Success = true,
            Code = "COLLECTOR_OPENED",
            Message = "采集卡已打开"
        };
        var httpClient = CreateHttpClient(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent(expected) });
        var launcher = new LauncherHttpClient("http://localhost:5135", httpClient);

        var result = await launcher.OpenDevice();

        Assert.True(result.Success);
        Assert.Equal("COLLECTOR_OPENED", result.Code);
        Assert.Equal("采集卡已打开", result.Message);
    }

    [Fact]
    public async Task CloseDevice_Success_ReturnsCommandResult()
    {
        var expected = new CommandResult
        {
            Success = true,
            Code = "COLLECTOR_CLOSED",
            Message = "采集卡已关闭"
        };
        var httpClient = CreateHttpClient(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent(expected) });
        var launcher = new LauncherHttpClient("http://localhost:5135", httpClient);

        var result = await launcher.CloseDevice();

        Assert.True(result.Success);
        Assert.Equal("COLLECTOR_CLOSED", result.Code);
    }

    [Fact]
    public async Task StartAcquisition_Success_ReturnsCommandResult()
    {
        var expected = new CommandResult
        {
            Success = true,
            Code = "AD_STARTED",
            Message = "采集已开始"
        };
        var httpClient = CreateHttpClient(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent(expected) });
        var launcher = new LauncherHttpClient("http://localhost:5135", httpClient);

        var result = await launcher.StartAcquisition();

        Assert.True(result.Success);
        Assert.Equal("AD_STARTED", result.Code);
    }

    [Fact]
    public async Task StopAcquisition_Success_ReturnsCommandResult()
    {
        var expected = new CommandResult
        {
            Success = true,
            Code = "AD_STOPPED",
            Message = "采集已停止"
        };
        var httpClient = CreateHttpClient(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent(expected) });
        var launcher = new LauncherHttpClient("http://localhost:5135", httpClient);

        var result = await launcher.StopAcquisition();

        Assert.True(result.Success);
    }

    [Fact]
    public async Task LaserConnect_Success_ReturnsCommandResult()
    {
        var expected = new CommandResult
        {
            Success = true,
            Code = "LASER_CONNECTED",
            Message = "激光器已连接"
        };
        var httpClient = CreateHttpClient(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent(expected) });
        var launcher = new LauncherHttpClient("http://localhost:5135", httpClient);

        var result = await launcher.LaserConnect();

        Assert.True(result.Success);
        Assert.Equal("LASER_CONNECTED", result.Code);
    }

    [Fact]
    public async Task LaserOn_Success_ReturnsCommandResult()
    {
        var expected = new CommandResult
        {
            Success = true,
            Code = "LASER_ON",
            Message = "激光已开启"
        };
        var httpClient = CreateHttpClient(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent(expected) });
        var launcher = new LauncherHttpClient("http://localhost:5135", httpClient);

        var result = await launcher.LaserOn();

        Assert.True(result.Success);
    }

    [Fact]
    public async Task LaserOff_Success_ReturnsCommandResult()
    {
        var expected = new CommandResult
        {
            Success = true,
            Code = "LASER_OFF",
            Message = "激光已关闭"
        };
        var httpClient = CreateHttpClient(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent(expected) });
        var launcher = new LauncherHttpClient("http://localhost:5135", httpClient);

        var result = await launcher.LaserOff();

        Assert.True(result.Success);
    }

    [Fact]
    public async Task Command_ConnectionError_ReturnsErrorResult()
    {
        var httpClient = CreateHttpClient(_ =>
            throw new HttpRequestException("No connection"));
        var launcher = new LauncherHttpClient("http://localhost:5135", httpClient);

        var result = await launcher.OpenDevice();

        Assert.False(result.Success);
        Assert.Equal("CONNECTION_ERROR", result.Code);
        Assert.Contains("无法连接到 WebAPI", result.Message);
    }

    [Fact]
    public async Task Command_HttpError_ReturnsErrorResult()
    {
        var errorResult = new CommandResult
        {
            Success = false,
            Code = "INTERNAL_ERROR",
            Message = "Server error"
        };
        var httpClient = CreateHttpClient(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = JsonContent(errorResult)
            });
        var launcher = new LauncherHttpClient("http://localhost:5135", httpClient);

        var result = await launcher.OpenDevice();

        Assert.False(result.Success);
        Assert.Equal("INTERNAL_ERROR", result.Code);
    }

    [Fact]
    public async Task GetSystemState_Success_ReturnsState()
    {
        var state = new SystemStateDto
        {
            Server = new ServerStateDto { IsApiAlive = true },
            Collector = new CollectorStateDto
            {
                ProcessConnected = true,
                DeviceOpened = true,
                Acquiring = false
            },
            Laser = new LaserStateDto
            {
                SerialConnected = false,
                EmissionOn = false
            },
            Timestamp = new DateTime(2026, 1, 15, 10, 30, 0)
        };
        var httpClient = CreateHttpClient(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent(state) });
        var launcher = new LauncherHttpClient("http://localhost:5135", httpClient);

        var result = await launcher.GetSystemState();

        Assert.NotNull(result);
        Assert.True(result.Server.IsApiAlive);
        Assert.True(result.Collector.ProcessConnected);
        Assert.True(result.Collector.DeviceOpened);
        Assert.False(result.Collector.Acquiring);
        Assert.False(result.Laser.SerialConnected);
    }

    [Fact]
    public async Task GetSystemState_ConnectionError_ReturnsDefaultState()
    {
        var httpClient = CreateHttpClient(_ =>
            throw new HttpRequestException("No connection"));
        var launcher = new LauncherHttpClient("http://localhost:5135", httpClient);

        var result = await launcher.GetSystemState();

        Assert.NotNull(result);
        Assert.False(result.Server.IsApiAlive);
        Assert.False(result.Collector.ProcessConnected);
    }

    [Fact]
    public async Task ShutdownWebApi_Success_ReturnsTrue()
    {
        var httpClient = CreateHttpClient(_ =>
            new HttpResponseMessage(HttpStatusCode.OK));
        var launcher = new LauncherHttpClient("http://localhost:5135", httpClient);

        var result = await launcher.ShutdownWebApi();

        Assert.True(result);
    }

    [Fact]
    public async Task ShutdownWebApi_Error_ReturnsFalse()
    {
        var httpClient = CreateHttpClient(_ =>
            throw new HttpRequestException("No connection"));
        var launcher = new LauncherHttpClient("http://localhost:5135", httpClient);

        var result = await launcher.ShutdownWebApi();

        Assert.False(result);
    }

    [Fact]
    public async Task LaserDisconnect_Success_ReturnsCommandResult()
    {
        var expected = new CommandResult
        {
            Success = true,
            Code = "LASER_DISCONNECTED",
            Message = "激光器已断开"
        };
        var httpClient = CreateHttpClient(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent(expected) });
        var launcher = new LauncherHttpClient("http://localhost:5135", httpClient);

        var result = await launcher.LaserDisconnect();

        Assert.True(result.Success);
        Assert.Equal("LASER_DISCONNECTED", result.Code);
    }
}
