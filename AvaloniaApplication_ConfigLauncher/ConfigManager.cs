using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using SharedModels;

namespace AvaloniaApplication_ConfigLauncher;

public class ConfigManager
{
    private readonly string _baseDirectory;

    public ConfigManager(string baseDirectory)
    {
        _baseDirectory = baseDirectory;
    }

    private string AppSettingsPath => Path.Combine(_baseDirectory, "appsettings.json");
    private string ConfiguredMarkerPath => Path.Combine(_baseDirectory, ".mqtt_configured");

    public bool HasExistingConfig()
    {
        return File.Exists(ConfiguredMarkerPath);
    }

    public void MarkConfigured()
    {
        File.Create(ConfiguredMarkerPath).Dispose();
    }

    public MqttSettings LoadConfig()
    {
        if (!File.Exists(AppSettingsPath))
        {
            return new MqttSettings();
        }

        var json = File.ReadAllText(AppSettingsPath);
        var root = JsonNode.Parse(json);
        var mqttNode = root?["Mqtt"];
        if (mqttNode == null)
        {
            return new MqttSettings();
        }

        return mqttNode.Deserialize<MqttSettings>() ?? new MqttSettings();
    }

    public void SaveConfig(MqttSettings settings)
    {
        JsonNode root;

        if (File.Exists(AppSettingsPath))
        {
            var existingJson = File.ReadAllText(AppSettingsPath);
            root = JsonNode.Parse(existingJson) ?? new JsonObject();
        }
        else
        {
            root = new JsonObject
            {
                ["Logging"] = new JsonObject
                {
                    ["LogLevel"] = new JsonObject
                    {
                        ["Default"] = "Information",
                        ["Microsoft.AspNetCore"] = "Warning"
                    }
                },
                ["AllowedHosts"] = "*"
            };
        }

        root["Mqtt"] = JsonSerializer.SerializeToNode(settings);
        var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(AppSettingsPath, json);
    }

    public string LoadBaseUrl()
    {
        if (!File.Exists(AppSettingsPath))
        {
            return "http://localhost:5135";
        }

        var json = File.ReadAllText(AppSettingsPath);
        var root = JsonNode.Parse(json);
        var launcherNode = root?["Launcher"];
        var baseUrlNode = launcherNode?["BaseUrl"];
        if (baseUrlNode == null)
        {
            return "http://localhost:5135";
        }

        return baseUrlNode.GetValue<string>();
    }

    public void SaveBaseUrl(string url)
    {
        JsonNode root;

        if (File.Exists(AppSettingsPath))
        {
            var existingJson = File.ReadAllText(AppSettingsPath);
            root = JsonNode.Parse(existingJson) ?? new JsonObject();
        }
        else
        {
            root = new JsonObject();
        }

        var launcherNode = root["Launcher"] as JsonObject;
        if (launcherNode == null)
        {
            launcherNode = new JsonObject();
            root["Launcher"] = launcherNode;
        }

        launcherNode["BaseUrl"] = url;
        var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(AppSettingsPath, json);
    }
}
