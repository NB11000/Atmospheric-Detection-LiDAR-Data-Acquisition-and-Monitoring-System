using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SharedModels;

namespace AvaloniaApplication_ConfigLauncher;

/// <summary>
/// Issue 04 Part A: HTTP client for communicating with the WebAPI.
/// All methods handle connection errors gracefully by returning
/// CommandResult with Success=false.
/// </summary>
public class LauncherHttpClient
{
    private readonly string _baseUrl;
    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public LauncherHttpClient(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    /// <summary>
    /// Internal constructor for testing with a provided HttpClient.
    /// </summary>
    internal LauncherHttpClient(string baseUrl, HttpClient httpClient)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _httpClient = httpClient;
    }

    // ── System ────────────────────────────────────────────────

    /// <summary>
    /// GET {baseUrl}/api/system/state → SystemStateDto
    /// </summary>
    public async Task<SystemStateDto> GetSystemState()
    {
        try
        {
            var response = await _httpClient.GetAsync(_baseUrl + "/api/system/state");
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<SystemStateDto>(json, _jsonOptions) ?? new SystemStateDto();
        }
        catch
        {
            return new SystemStateDto();
        }
    }

    /// <summary>
    /// POST {baseUrl}/api/system/shutdown → true if 200 OK
    /// </summary>
    public async Task<bool> ShutdownWebApi()
    {
        try
        {
            var response = await _httpClient.PostAsync(_baseUrl + "/api/system/shutdown", null);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    // ── Collector ─────────────────────────────────────────────

    public Task<CommandResult> OpenDevice() => PostCollectorCommand("open");
    public Task<CommandResult> CloseDevice() => PostCollectorCommand("close");
    public Task<CommandResult> StartAcquisition() => PostCollectorCommand("start");
    public Task<CommandResult> StopAcquisition() => PostCollectorCommand("stop");

    // ── Laser ─────────────────────────────────────────────────

    public Task<CommandResult> LaserConnect() => PostLaserCommand("connect");
    public Task<CommandResult> LaserDisconnect() => PostLaserCommand("disconnect");
    public Task<CommandResult> LaserOn() => PostLaserCommand("on");
    public Task<CommandResult> LaserOff() => PostLaserCommand("off");

    // ── CaptureCard Config ───────────────────────────────────

    public Task<CaptureCardConfig?> GetCaptureCardConfig()
        => PostConfigReadAsync<CaptureCardConfig>("/api/collector/command/config/read");

    public Task<CaptureCardConfig?> UpdateCaptureCardConfig(CaptureCardConfig config)
        => PostConfigAsync<CaptureCardConfig>("/api/collector/command/config/update", config);

    public Task<CaptureCardConfig?> GetDefaultCaptureCardConfig()
        => GetConfigAsync<CaptureCardConfig>("/api/collector/command/config/default");

    // ── Radar Config ─────────────────────────────────────────

    public Task<RadarConfig?> GetRadarConfig()
        => PostConfigReadAsync<RadarConfig>("/api/laser/config/read");

    public Task<RadarConfig?> UpdateRadarConfig(RadarConfig config)
        => PostConfigAsync<RadarConfig>("/api/laser/config/update", config);

    public Task<RadarConfig?> GetDefaultRadarConfig()
        => GetConfigAsync<RadarConfig>("/api/laser/config/default");

    // ── Lidar Config ─────────────────────────────────────────

    public Task<LidarAlgorithmConfig?> GetLidarConfig()
        => PostConfigReadAsync<LidarAlgorithmConfig>("/api/lidar/config/read");

    public Task<LidarAlgorithmConfig?> UpdateLidarConfig(LidarAlgorithmConfig config)
        => PostConfigAsync<LidarAlgorithmConfig>("/api/lidar/config/update", config);

    public Task<LidarAlgorithmConfig?> GetDefaultLidarConfig()
        => GetConfigAsync<LidarAlgorithmConfig>("/api/lidar/config/default");

    // ── Persistence Config ───────────────────────────────────

    public Task<PersistenceSettings?> GetPersistenceConfig()
        => PostConfigReadAsync<PersistenceSettings>("/api/persistence/config/read");

    public Task<PersistenceSettings?> UpdatePersistenceConfig(PersistenceSettings config)
        => PostConfigAsync<PersistenceSettings>("/api/persistence/config/update", config);

    public Task<PersistenceSettings?> GetDefaultPersistenceConfig()
        => GetConfigAsync<PersistenceSettings>("/api/persistence/config/default");

    // ── Helpers ──────────────────────────────────────────────

    private async Task<CommandResult> PostCollectorCommand(string command)
    {
        return await PostCommandAsync($"/api/collector/command/{command}");
    }

    private async Task<CommandResult> PostLaserCommand(string command)
    {
        return await PostCommandAsync($"/api/laser/{command}");
    }

    private async Task<CommandResult> PostCommandAsync(string path)
    {
        try
        {
            var content = new StringContent(
                "{}",
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync(_baseUrl + path, content);
            var json = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                return JsonSerializer.Deserialize<CommandResult>(json, _jsonOptions)
                    ?? new CommandResult { Success = false, Code = "PARSE_ERROR", Message = "无法解析服务器响应" };
            }

            // Try to deserialize error body as CommandResult
            try
            {
                return JsonSerializer.Deserialize<CommandResult>(json, _jsonOptions)
                    ?? new CommandResult { Success = false, Code = "HTTP_ERROR", Message = $"HTTP {(int)response.StatusCode}" };
            }
            catch
            {
                return new CommandResult
                {
                    Success = false,
                    Code = "HTTP_ERROR",
                    Message = $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}"
                };
            }
        }
        catch (Exception ex)
        {
            return new CommandResult
            {
                Success = false,
                Code = "CONNECTION_ERROR",
                Message = $"无法连接到 WebAPI: {ex.Message}"
            };
        }
    }

    private async Task<T?> GetConfigAsync<T>(string path) where T : class
    {
        try
        {
            var response = await _httpClient.GetAsync(_baseUrl + path);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<T>(json, _jsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private async Task<T?> PostConfigAsync<T>(string path, object body) where T : class
    {
        try
        {
            var content = new StringContent(
                JsonSerializer.Serialize(body, _jsonOptions),
                Encoding.UTF8,
                "application/json");
            var response = await _httpClient.PostAsync(_baseUrl + path, content);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<T>(json, _jsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private async Task<T?> PostConfigReadAsync<T>(string path) where T : class
    {
        try
        {
            var content = new StringContent(
                "{}",
                Encoding.UTF8,
                "application/json");
            var response = await _httpClient.PostAsync(_baseUrl + path, content);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<T>(json, _jsonOptions);
        }
        catch
        {
            return null;
        }
    }
}
