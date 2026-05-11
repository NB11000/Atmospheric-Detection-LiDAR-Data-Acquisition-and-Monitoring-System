using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaApplication_ConfigLauncher;
using Xunit;

namespace AvaloniaApplication_ConfigLauncher.Tests;

/// <summary>
/// Tests for WebApiProcessManager (Issue 03 Part B).
/// </summary>
public class WebApiProcessManagerTests
{
    // RED phase: these tests should FAIL because WebApiProcessManager doesn't exist yet.

    [Fact]
    public void Constructor_Stores_WebApiDirectory()
    {
        var dir = @"C:\TestWebApi";
        var manager = new WebApiProcessManager(dir);

        Assert.NotNull(manager);
    }

    [Fact]
    public async Task WaitUntilReadyAsync_ReturnsTrue_WhenHttp200()
    {
        // Arrange: fake handler returns 200 OK
        var fakeHandler = new FakeHttpMessageHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.OK)
        };
        var httpClient = new HttpClient(fakeHandler);
        var manager = new WebApiProcessManager(@"C:\Test", httpClient);

        // Act
        var result = await manager.WaitUntilReadyAsync("http://localhost:5135", timeoutSec: 2);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task WaitUntilReadyAsync_ReturnsFalse_OnTimeout()
    {
        // Arrange: fake handler always returns 503 (never ready)
        var fakeHandler = new FakeHttpMessageHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        };
        var httpClient = new HttpClient(fakeHandler);
        var manager = new WebApiProcessManager(@"C:\Test", httpClient);

        // Act
        var result = await manager.WaitUntilReadyAsync("http://localhost:5135", timeoutSec: 1);

        // Assert: should timeout after 1 second
        Assert.False(result);
    }

    [Fact]
    public async Task StopAsync_SendsShutdownPostRequest()
    {
        // Arrange: POST returns 200 OK
        var fakeHandler = new FakeHttpMessageHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.OK)
        };
        var httpClient = new HttpClient(fakeHandler);
        var manager = new WebApiProcessManager(@"C:\Test", httpClient);

        // Act
        var result = await manager.StopAsync("http://localhost:5135", timeoutSec: 2);

        // Assert: verify POST was sent to the shutdown endpoint
        Assert.NotNull(fakeHandler.LastRequest);
        Assert.Equal(HttpMethod.Post, fakeHandler.LastRequest!.Method);
        Assert.Equal(
            "http://localhost:5135/api/system/shutdown",
            fakeHandler.LastRequest.RequestUri!.ToString());
    }

    [Fact]
    public async Task StopAsync_ReturnsFalse_WhenHttpUnreachable()
    {
        // Arrange: fake handler throws to simulate unreachable server
        var fakeHandler = new FakeHttpMessageHandler
        {
            ThrowException = new HttpRequestException("Connection refused")
        };
        var httpClient = new HttpClient(fakeHandler);
        var manager = new WebApiProcessManager(@"C:\Test", httpClient);

        // Act
        var result = await manager.StopAsync("http://localhost:5135", timeoutSec: 1);

        // Assert: when HTTP unreachable, should return false
        Assert.False(result);
    }

    /// <summary>
    /// Simple fake HttpMessageHandler that returns a predetermined response
    /// or throws an exception.
    /// </summary>
    private class FakeHttpMessageHandler : HttpMessageHandler
    {
        public HttpResponseMessage? Response { get; set; }
        public HttpRequestMessage? LastRequest { get; private set; }
        public Exception? ThrowException { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;

            if (ThrowException != null)
            {
                return Task.FromException<HttpResponseMessage>(ThrowException);
            }

            return Task.FromResult(
                Response ?? new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
