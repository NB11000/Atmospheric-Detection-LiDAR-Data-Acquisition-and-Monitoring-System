using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WebAPI.Controllers;
using WebAPI.Service;
using Xunit;

namespace WebAPI.Tests;

/// <summary>
/// Tests for POST api/system/shutdown endpoint (Issue 03 Part A).
/// </summary>
public class SystemStateControllerShutdownTests
{
    /// <summary>
    /// Fake IHostApplicationLifetime that records whether StopApplication was called.
    /// </summary>
    private class FakeHostApplicationLifetime : IHostApplicationLifetime
    {
        public bool StopApplicationCalled { get; private set; }

        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
            StopApplicationCalled = true;
        }
    }

    [Fact]
    public void Shutdown_Endpoint_Exists_And_Calls_StopApplication()
    {
        // RED phase: this test should FAIL because the Shutdown method
        // does not exist on SystemStateController yet.

        var fakeLifetime = new FakeHostApplicationLifetime();
        var logger = NullLogger<SystemStateService>.Instance;
        var stateService = new SystemStateService(logger);
        var controller = new SystemStateController(stateService, fakeLifetime);

        // Act
        var result = controller.Shutdown();

        // Assert
        Assert.True(fakeLifetime.StopApplicationCalled,
            "Expected StopApplication() to be called on IHostApplicationLifetime.");

        var okResult = Assert.IsType<OkResult>(result);
        Assert.Equal(200, okResult.StatusCode);
    }
}
