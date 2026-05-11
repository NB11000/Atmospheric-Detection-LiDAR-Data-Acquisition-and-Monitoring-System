using AvaloniaApplication_ConfigLauncher;
using SharedModels;
using System.Text.Json.Nodes;
using Xunit;

namespace AvaloniaApplication_ConfigLauncher.Tests;

public class ConfigManagerTests
{
    [Fact]
    public void HasExistingConfig_FileDoesNotExist_ReturnsFalse()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var configManager = new ConfigManager(tempDir);

            // Act
            var result = configManager.HasExistingConfig();

            // Assert
            Assert.False(result);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void MarkConfigured_ThenHasExistingConfig_ReturnsTrue()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var configManager = new ConfigManager(tempDir);

            // Act
            configManager.MarkConfigured();
            var result = configManager.HasExistingConfig();

            // Assert
            Assert.True(result);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void SaveConfig_ThenLoadConfig_ReturnsSameFields()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var configManager = new ConfigManager(tempDir);
            var original = new MqttSettings
            {
                BrokerHost = "mqtt.example.com",
                BrokerPort = 8883,
                MachineId = "test-machine",
                Username = "user1",
                Password = "pass1",
                RpcTimeoutSeconds = 30,
                ReconnectDelaySeconds = 10,
                UseTls = true,
                AllowUntrustedCertificates = true,
                CaCertificatePath = "/path/to/ca.crt",
                WaveformPublishIntervalMs = 200
            };

            // Act
            configManager.SaveConfig(original);
            var loaded = configManager.LoadConfig();

            // Assert
            Assert.Equal(original.BrokerHost, loaded.BrokerHost);
            Assert.Equal(original.BrokerPort, loaded.BrokerPort);
            Assert.Equal(original.MachineId, loaded.MachineId);
            Assert.Equal(original.Username, loaded.Username);
            Assert.Equal(original.Password, loaded.Password);
            Assert.Equal(original.RpcTimeoutSeconds, loaded.RpcTimeoutSeconds);
            Assert.Equal(original.ReconnectDelaySeconds, loaded.ReconnectDelaySeconds);
            Assert.Equal(original.UseTls, loaded.UseTls);
            Assert.Equal(original.AllowUntrustedCertificates, loaded.AllowUntrustedCertificates);
            Assert.Equal(original.CaCertificatePath, loaded.CaCertificatePath);
            Assert.Equal(original.WaveformPublishIntervalMs, loaded.WaveformPublishIntervalMs);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void SaveConfig_OverwriteBrokerHost_PreservesLoggingNode()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var configManager = new ConfigManager(tempDir);
            var settings = new MqttSettings { BrokerHost = "first.example.com", BrokerPort = 1883 };
            configManager.SaveConfig(settings);

            // Modify and save again
            settings.BrokerHost = "second.example.com";
            configManager.SaveConfig(settings);

            // Act: verify Logging node still exists
            var json = File.ReadAllText(Path.Combine(tempDir, "appsettings.json"));
            var root = JsonNode.Parse(json);

            // Assert
            Assert.NotNull(root);
            Assert.NotNull(root!["Logging"]);
            Assert.NotNull(root!["AllowedHosts"]);
            Assert.NotNull(root!["Mqtt"]);
            Assert.Equal("second.example.com", root!["Mqtt"]!["BrokerHost"]!.GetValue<string>());
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void SaveConfig_FileDoesNotExist_CreatesCompleteJson()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var configManager = new ConfigManager(tempDir);
            var settings = new MqttSettings { BrokerHost = "new.example.com", BrokerPort = 8883 };

            // Act
            configManager.SaveConfig(settings);
            var json = File.ReadAllText(Path.Combine(tempDir, "appsettings.json"));
            var root = JsonNode.Parse(json);

            // Assert
            Assert.NotNull(root);
            Assert.NotNull(root!["Logging"]);
            Assert.NotNull(root!["AllowedHosts"]);
            Assert.NotNull(root!["Mqtt"]);
            Assert.Equal("new.example.com", root!["Mqtt"]!["BrokerHost"]!.GetValue<string>());
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void LoadBaseUrl_NoConfig_ReturnsDefault()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var configManager = new ConfigManager(tempDir);

            // Act
            var url = configManager.LoadBaseUrl();

            // Assert
            Assert.Equal("http://localhost:5135", url);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void SaveBaseUrl_ThenLoadBaseUrl_ReturnsSameValue()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var configManager = new ConfigManager(tempDir);
            var expectedUrl = "http://192.168.1.100:5000";

            // Act
            configManager.SaveBaseUrl(expectedUrl);
            var actualUrl = configManager.LoadBaseUrl();

            // Assert
            Assert.Equal(expectedUrl, actualUrl);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void SaveConfig_DoesNotDestroy_LauncherNode()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var configManager = new ConfigManager(tempDir);
            configManager.SaveBaseUrl("http://custom:9999");
            var settings = new MqttSettings { BrokerHost = "mqtt.example.com" };

            // Act
            configManager.SaveConfig(settings);
            var url = configManager.LoadBaseUrl();

            // Assert
            Assert.Equal("http://custom:9999", url);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
