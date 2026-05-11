using System;
using System.Diagnostics;
using System.Threading.Tasks;
using AvaloniaApplication_ConfigLauncher;
using AvaloniaApplication_ConfigLauncher.ViewModels;
using SharedModels;
using Xunit;

namespace AvaloniaApplication_ConfigLauncher.Tests;

/// <summary>
/// Tests for ConfigViewModel process integration (Issue 03 Part C).
/// </summary>
public class ConfigViewModelProcessTests
{
    private static (ConfigManager, string) CreateTempConfigManager()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var cm = new ConfigManager(tempDir);
        return (cm, tempDir);
    }

    [Fact]
    public async Task SaveAndStart_Calls_Start_And_WaitUntilReady()
    {
        // Arrange
        var (configManager, tempDir) = CreateTempConfigManager();
        try
        {
            var fakePm = new FakeProcessManager { WaitUntilReadyResult = true };
            var tabSwitched = false;
            var vm = new ConfigViewModel(configManager, fakePm, () => tabSwitched = true);
            vm.BrokerHost = "test.mqtt.com";
            configManager.SaveBaseUrl("http://localhost:5135");

            // Act
            vm.SaveAndStartCommand.Execute(null);

            // Allow async WaitUntilReady to complete
            await Task.Delay(100);

            // Assert
            Assert.True(fakePm.StartCalled, "Expected Start() to be called.");
            Assert.True(fakePm.WaitUntilReadyCalled, "Expected WaitUntilReadyAsync() to be called.");
            Assert.True(tabSwitched, "Expected switch-to-control-tab callback to be invoked.");
            Assert.True(configManager.HasExistingConfig(), "Expected marker to be created.");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void SaveAndStart_OnTimeout_ShowsError_DoesNotSwitchTab()
    {
        // Arrange
        var (configManager, tempDir) = CreateTempConfigManager();
        try
        {
            var fakePm = new FakeProcessManager { WaitUntilReadyResult = false };
            var tabSwitched = false;
            var vm = new ConfigViewModel(configManager, fakePm, () => tabSwitched = true);
            vm.BrokerHost = "test.mqtt.com";
            configManager.SaveBaseUrl("http://localhost:5135");

            // Act
            vm.SaveAndStartCommand.Execute(null);

            // Assert
            Assert.True(fakePm.StartCalled);
            Assert.True(fakePm.WaitUntilReadyCalled);
            Assert.False(tabSwitched, "Should NOT switch tab on timeout.");
            Assert.Contains("超时", vm.StatusMessage);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task UseExistingAndStart_Calls_Start_And_WaitUntilReady()
    {
        // Arrange
        var (configManager, tempDir) = CreateTempConfigManager();
        try
        {
            // Pre-configure so we're in Mode B
            configManager.SaveConfig(new MqttSettings
            {
                BrokerHost = "existing.mqtt.com",
                BrokerPort = 8883,
                MachineId = "test-machine"
            });
            configManager.MarkConfigured();
            configManager.SaveBaseUrl("http://localhost:5135");

            var fakePm = new FakeProcessManager { WaitUntilReadyResult = true };
            var tabSwitched = false;
            var vm = new ConfigViewModel(configManager, fakePm, () => tabSwitched = true);

            // Should be in Mode B
            Assert.True(vm.IsModeB);

            // Act
            vm.UseExistingAndStartCommand.Execute(null);
            await Task.Delay(100);

            // Assert
            Assert.True(fakePm.StartCalled, "Expected Start() to be called.");
            Assert.True(fakePm.WaitUntilReadyCalled, "Expected WaitUntilReadyAsync() to be called.");
            Assert.True(tabSwitched, "Expected switch-to-control-tab callback.");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void UseExistingAndStart_OnTimeout_ShowsError()
    {
        // Arrange
        var (configManager, tempDir) = CreateTempConfigManager();
        try
        {
            configManager.SaveConfig(new MqttSettings
            {
                BrokerHost = "existing.mqtt.com",
                BrokerPort = 8883,
                MachineId = "test-machine"
            });
            configManager.MarkConfigured();
            configManager.SaveBaseUrl("http://localhost:5135");

            var fakePm = new FakeProcessManager { WaitUntilReadyResult = false };
            var tabSwitched = false;
            var vm = new ConfigViewModel(configManager, fakePm, () => tabSwitched = true);

            // Act
            vm.UseExistingAndStartCommand.Execute(null);

            // Assert
            Assert.True(fakePm.StartCalled);
            Assert.False(tabSwitched);
            Assert.Contains("超时", vm.StatusMessage);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// Fake IWebApiProcessManager for testing ViewModel behavior.
    /// </summary>
    private class FakeProcessManager : IWebApiProcessManager
    {
        public bool StartCalled { get; private set; }
        public bool WaitUntilReadyCalled { get; private set; }
        public bool WaitUntilReadyResult { get; set; } = true;
        public string? WaitUntilReadyBaseUrl { get; private set; }
        public bool IsProcessRunning { get; set; }

        public Process Start()
        {
            StartCalled = true;
            // Return a fake Process object that's already "exited"
            // Use a non-null approach via a simple Process reference
            return new Process();
        }

        public Task<bool> WaitUntilReadyAsync(string baseUrl, int timeoutSec = 30)
        {
            WaitUntilReadyCalled = true;
            WaitUntilReadyBaseUrl = baseUrl;
            return Task.FromResult(WaitUntilReadyResult);
        }

        public Task<bool> StopAsync(string baseUrl, int timeoutSec = 10)
        {
            return Task.FromResult(true);
        }
    }
}
