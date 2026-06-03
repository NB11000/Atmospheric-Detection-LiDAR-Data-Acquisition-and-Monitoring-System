using AvaloniaApplication_ConfigLauncher.ViewModels;
using SharedModels;
using Xunit;

namespace AvaloniaApplication_ConfigLauncher.Tests;

public class ConfigViewModelTests
{
    private static (ConfigManager, string) CreateTempConfigManager()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var cm = new ConfigManager(tempDir);
        return (cm, tempDir);
    }

    [Fact]
    public void ModeA_BrokerHost_Empty_Disables_Save()
    {
        // Arrange
        var (configManager, tempDir) = CreateTempConfigManager();
        try
        {
            // Act
            var vm = new ConfigViewModel(configManager);

            // Assert - no existing config, should be Mode A
            Assert.True(vm.IsModeA);
            Assert.False(vm.IsModeB);
            // BrokerHost starts empty, save should be disabled
            Assert.False(vm.IsSaveEnabled);
            Assert.Equal("", vm.BrokerHost);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ModeA_BrokerHost_Filled_Enables_Save()
    {
        // Arrange
        var (configManager, tempDir) = CreateTempConfigManager();
        try
        {
            var vm = new ConfigViewModel(configManager);

            // Act
            vm.BrokerHost = "mqtt.example.com";

            // Assert
            Assert.True(vm.IsSaveEnabled);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ModeA_Defaults_Correct()
    {
        // Arrange & Act
        var (configManager, tempDir) = CreateTempConfigManager();
        try
        {
            var vm = new ConfigViewModel(configManager);

            // Assert
            Assert.Equal(Environment.MachineName, vm.MachineId);
            Assert.Equal("8883", vm.BrokerPortText);
            Assert.True(vm.UseTls);
            Assert.Equal("60", vm.RpcTimeoutSecondsText);
            Assert.Equal("5", vm.ReconnectDelaySecondsText);
            Assert.Equal("100", vm.WaveformPublishIntervalMsText);
            Assert.False(vm.IsAdvancedExpanded);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void SaveConfig_WritesMarker_And_Verifies()
    {
        // Arrange
        var (configManager, tempDir) = CreateTempConfigManager();
        try
        {
            var vm = new ConfigViewModel(configManager);
            vm.BrokerHost = "test.mqtt.com";
            vm.BrokerPortText = "8883";
            vm.MachineId = "test-machine";
            vm.Username = "user1";
            vm.Password = "pass1";
            vm.UseTls = true;

            // Act
            vm.SaveAndStartCommand.Execute(null);

            // Assert - after save, switches to Mode B
            Assert.True(vm.IsModeB);
            Assert.False(vm.IsModeA);
            Assert.StartsWith("配置已保存", vm.StatusMessage);

            // Verify ConfigManager
            Assert.True(configManager.HasExistingConfig());
            var loaded = configManager.LoadConfig();
            Assert.Equal("test.mqtt.com", loaded.BrokerHost);
            Assert.Equal(8883, loaded.BrokerPort);
            Assert.Equal("test-machine", loaded.MachineId);
            Assert.Equal("user1", loaded.Username);
            Assert.Equal("pass1", loaded.Password);
            Assert.True(loaded.UseTls);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ModeB_LoadsExistingConfig_Summary()
    {
        // Arrange
        var (configManager, tempDir) = CreateTempConfigManager();
        try
        {
            // First save a config
            configManager.SaveConfig(new MqttSettings
            {
                BrokerHost = "existing.mqtt.com",
                BrokerPort = 8883,
                MachineId = "my-machine",
                Username = "adminuser",
                Password = "secret",
                UseTls = true
            });
            configManager.MarkConfigured();
            configManager.SaveBaseUrl("http://192.168.1.50:5000");

            // Act
            var vm = new ConfigViewModel(configManager);

            // Assert - Mode B
            Assert.True(vm.IsModeB);
            Assert.False(vm.IsModeA);
            Assert.Equal("existing.mqtt.com:8883", vm.SummaryBrokerDisplay);
            Assert.Equal("my-machine", vm.SummaryMachineId);
            Assert.Equal("a*******r", vm.SummaryUsername); // first+last, rest asterisks
            Assert.Equal("已启用", vm.SummaryTlsStatus);
            Assert.Equal("http://192.168.1.50:5000", vm.SummaryBaseUrl);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void MaskUsername_AllButFirstAndLast_Masked()
    {
        // Normal case: "username" (8 chars) -> u + 6* + e = u******e
        Assert.Equal("u******e", ConfigViewModel.MaskUsername("username"));
        // Short: 1 char
        Assert.Equal("a", ConfigViewModel.MaskUsername("a"));
        // Short: 2 chars
        Assert.Equal("ab", ConfigViewModel.MaskUsername("ab"));
        // Empty
        Assert.Equal("", ConfigViewModel.MaskUsername(""));
        // 3 chars: "abc" -> a + 1* + c = a*c
        Assert.Equal("a*c", ConfigViewModel.MaskUsername("abc"));
    }

    [Fact]
    public void SwitchToEdit_Prefills_From_ExistingConfig()
    {
        // Arrange
        var (configManager, tempDir) = CreateTempConfigManager();
        try
        {
            configManager.SaveConfig(new MqttSettings
            {
                BrokerHost = "edit.mqtt.com",
                BrokerPort = 1883,
                MachineId = "edit-machine",
                Username = "editor",
                Password = "pw",
                UseTls = false,
                AllowUntrustedCertificates = true,
                CaCertificatePath = "/etc/ca.crt",
                RpcTimeoutSeconds = 30,
                ReconnectDelaySeconds = 10,
                WaveformPublishIntervalMs = 200
            });
            configManager.MarkConfigured();

            var vm = new ConfigViewModel(configManager);
            Assert.True(vm.IsModeB); // starts in Mode B

            // Act
            vm.SwitchToEditCommand.Execute(null);

            // Assert - switches to Mode A with pre-filled values
            Assert.True(vm.IsModeA);
            Assert.False(vm.IsModeB);
            Assert.Equal("edit.mqtt.com", vm.BrokerHost);
            Assert.Equal("1883", vm.BrokerPortText);
            Assert.Equal("edit-machine", vm.MachineId);
            Assert.Equal("editor", vm.Username);
            Assert.Equal("pw", vm.Password);
            Assert.False(vm.UseTls);
            Assert.True(vm.AllowUntrustedCertificates);
            Assert.Equal("/etc/ca.crt", vm.CaCertificatePath);
            Assert.Equal("30", vm.RpcTimeoutSecondsText);
            Assert.Equal("10", vm.ReconnectDelaySecondsText);
            Assert.Equal("200", vm.WaveformPublishIntervalMsText);
            Assert.True(vm.IsSaveEnabled);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void SaveConfig_PreservesLauncherNode()
    {
        // Arrange
        var (configManager, tempDir) = CreateTempConfigManager();
        try
        {
            // Save a base URL first
            configManager.SaveBaseUrl("http://test:9999");

            var vm = new ConfigViewModel(configManager);
            vm.BrokerHost = "new.mqtt.com";

            // Act
            vm.SaveAndStartCommand.Execute(null);

            // Assert - Launcher/BaseUrl preserved
            var loadedUrl = configManager.LoadBaseUrl();
            Assert.Equal("http://test:9999", loadedUrl);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ModeA_BaseUrl_Defaults_To_Localhost()
    {
        var (configManager, tempDir) = CreateTempConfigManager();
        try
        {
            var vm = new ConfigViewModel(configManager);

            Assert.Equal("http://localhost:5135", vm.BaseUrl);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void SaveAndStart_SavesBaseUrl_And_InvokesCallback()
    {
        var (configManager, tempDir) = CreateTempConfigManager();
        try
        {
            var vm = new ConfigViewModel(configManager);
            vm.BrokerHost = "test.mqtt.com";
            vm.BaseUrl = "http://192.168.1.100:5000";

            string? notifiedUrl = null;
            vm.NotifyBaseUrlChanged = url => notifiedUrl = url;

            vm.SaveAndStartCommand.Execute(null);

            var loadedUrl = configManager.LoadBaseUrl();
            Assert.Equal("http://192.168.1.100:5000", loadedUrl);
            Assert.Equal("http://192.168.1.100:5000", notifiedUrl);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void SwitchToEdit_RestoresBaseUrl()
    {
        var (configManager, tempDir) = CreateTempConfigManager();
        try
        {
            configManager.SaveConfig(new MqttSettings { BrokerHost = "host.com", BrokerPort = 8883 });
            configManager.MarkConfigured();
            configManager.SaveBaseUrl("http://custom:8080");

            var vm = new ConfigViewModel(configManager);
            Assert.True(vm.IsModeB);
            Assert.Equal("http://custom:8080", vm.SummaryBaseUrl);

            vm.SwitchToEditCommand.Execute(null);

            Assert.True(vm.IsModeA);
            Assert.Equal("http://custom:8080", vm.BaseUrl);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ModeB_BaseUrl_Loaded_From_Config()
    {
        var (configManager, tempDir) = CreateTempConfigManager();
        try
        {
            configManager.SaveConfig(new MqttSettings { BrokerHost = "host.com", BrokerPort = 8883 });
            configManager.MarkConfigured();
            configManager.SaveBaseUrl("http://remote:6000");

            var vm = new ConfigViewModel(configManager);

            Assert.True(vm.IsModeB);
            Assert.Equal("http://remote:6000", vm.SummaryBaseUrl);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void MainWindowViewModel_LoadsBaseUrl_OnConstruction()
    {
        // Arrange
        // Note: MainWindowViewModel uses AppDomain.CurrentDomain.BaseDirectory,
        // which is the test output dir. We test it works without crashing.
        // For proper integration testing, BaseUrl loads from whatever config exists.

        // Act
        var mwvm = new MainWindowViewModel();

        // Assert - should not throw, has a default value
        Assert.NotNull(mwvm.BaseUrl);
        Assert.NotNull(mwvm.ConfigVm);
        Assert.NotNull(mwvm.ControlVm);
        Assert.NotNull(mwvm.LogVm);
        Assert.Equal("未连接", mwvm.WebApiConnectionStatus);
    }
}
