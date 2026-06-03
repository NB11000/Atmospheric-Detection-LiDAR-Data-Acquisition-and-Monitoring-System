using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SharedModels;

namespace AvaloniaApplication_ConfigLauncher.ViewModels;

public partial class ConfigViewModel : ViewModelBase
{
    private readonly ConfigManager _configManager;
    private readonly IWebApiProcessManager? _processManager;
    private readonly Action? _onReady;
    private MqttSettings _loadedSettings = new();

    /// <summary>
    /// Issue 04 Part D: Callback to show restart confirmation dialog.
    /// Returns true if user chose "Yes", false otherwise.
    /// Set by MainWindowViewModel.
    /// </summary>
    public Func<Task<bool>>? ShowRestartConfirmationAsync { get; set; }

    /// <summary>
    /// Issue 04 Part D: Callback to perform full restart (shutdown + stop + start + wait).
    /// Returns true if restart succeeded.
    /// Set by MainWindowViewModel.
    /// </summary>
    public Func<Task<bool>>? RestartWebApiAsync { get; set; }

    /// <summary>
    /// Callback to request main window close. Set by MainWindowViewModel.
    /// </summary>
    public Action? ExitRequested { get; set; }

    /// <summary>
    /// Callback when BaseUrl is changed. Set by MainWindowViewModel.
    /// </summary>
    public Action<string>? NotifyBaseUrlChanged { get; set; }

    // ── Mode flags ──────────────────────────────────────────

    [ObservableProperty]
    private bool _isModeA;

    [ObservableProperty]
    private bool _isModeB;

    // ── Mode A: core form fields ────────────────────────────

    [ObservableProperty]
    private string _baseUrl = "http://localhost:5135";

    [ObservableProperty]
    private string _brokerHost = "";

    [ObservableProperty]
    private string _brokerPortText = "8883";

    [ObservableProperty]
    private string _machineId = Environment.MachineName;

    [ObservableProperty]
    private string _username = "";

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    private bool _useTls = true;

    // ── Mode A: advanced fields ─────────────────────────────

    [ObservableProperty]
    private bool _isAdvancedExpanded;

    [ObservableProperty]
    private bool _allowUntrustedCertificates;

    [ObservableProperty]
    private string _caCertificatePath = "";

    [ObservableProperty]
    private string _rpcTimeoutSecondsText = "60";

    [ObservableProperty]
    private string _reconnectDelaySecondsText = "5";

    [ObservableProperty]
    private string _waveformPublishIntervalMsText = "100";

    // ── Mode B: summary display properties ──────────────────

    [ObservableProperty]
    private string _summaryBrokerDisplay = "";

    [ObservableProperty]
    private string _summaryMachineId = "";

    [ObservableProperty]
    private string _summaryUsername = "";

    [ObservableProperty]
    private string _summaryTlsStatus = "";

    [ObservableProperty]
    private string _summaryBaseUrl = "";

    // ── Validation ──────────────────────────────────────────

    [ObservableProperty]
    private bool _isSaveEnabled;

    // ── Status ──────────────────────────────────────────────

    [ObservableProperty]
    private string _statusMessage = "";

    public ConfigViewModel(ConfigManager configManager)
        : this(configManager, null, null)
    {
    }

    public ConfigViewModel(
        ConfigManager configManager,
        IWebApiProcessManager? processManager,
        Action? onReady)
    {
        _configManager = configManager;
        _processManager = processManager;
        _onReady = onReady;

        BaseUrl = _configManager.LoadBaseUrl();

        if (_configManager.HasExistingConfig())
        {
            _loadedSettings = _configManager.LoadConfig();
            IsModeB = true;
            IsModeA = false;
            LoadSummaryFromSettings(_loadedSettings);
        }
        else
        {
            IsModeA = true;
            IsModeB = false;
            UpdateSaveEnabled();
        }
    }

    // ── Mode B: populate summary ────────────────────────────

    private void LoadSummaryFromSettings(MqttSettings settings)
    {
        SummaryBrokerDisplay = $"{settings.BrokerHost}:{settings.BrokerPort}";
        SummaryMachineId = settings.MachineId;
        SummaryUsername = MaskUsername(settings.Username);
        SummaryTlsStatus = settings.UseTls ? "已启用" : "未启用";
        SummaryBaseUrl = _configManager.LoadBaseUrl();
    }

    public static string MaskUsername(string username)
    {
        if (string.IsNullOrEmpty(username))
            return "";
        if (username.Length <= 2)
            return username;
        return username[0] + new string('*', username.Length - 2) + username[^1];
    }

    // ── Validation ──────────────────────────────────────────

    partial void OnBrokerHostChanged(string value)
    {
        UpdateSaveEnabled();
    }

    private void UpdateSaveEnabled()
    {
        IsSaveEnabled = !string.IsNullOrWhiteSpace(BrokerHost);
    }

    public bool IsUsernameEmpty => string.IsNullOrWhiteSpace(Username);

    // ── Commands ────────────────────────────────────────────

    [RelayCommand]
    private async Task SaveAndStart()
    {
        var settings = BuildSettingsFromForm();
        _configManager.SaveConfig(settings);
        _configManager.MarkConfigured();

        _configManager.SaveBaseUrl(BaseUrl);
        NotifyBaseUrlChanged?.Invoke(BaseUrl);

        var markerExists = _configManager.HasExistingConfig();
        var loadedBack = _configManager.LoadConfig();

        if (!markerExists || loadedBack.BrokerHost != settings.BrokerHost)
        {
            StatusMessage = "保存失败：文件写入验证未通过。";
            return;
        }

        _loadedSettings = settings;
        LoadSummaryFromSettings(settings);
        IsModeA = false;
        IsModeB = true;

        // Issue 04 Part D: Restart flow when launcher integration is available
        if (RestartWebApiAsync != null)
        {
            var shouldRestart = ShowRestartConfirmationAsync != null
                ? await ShowRestartConfirmationAsync()
                : true;

            if (shouldRestart)
            {
                StatusMessage = "正在重启 WebAPI...";
                var success = await RestartWebApiAsync();
                if (success)
                {
                    StatusMessage = "WebAPI 已重启并就绪。";
                }
                else
                {
                    StatusMessage = "WebAPI 重启失败，请检查服务状态。";
                }
            }
            else
            {
                StatusMessage = "配置已保存，WebAPI 未重启。";
            }
        }
        else
        {
            StatusMessage = "配置已保存，正在启动 WebAPI...";
            await StartWebApiAndWait();
        }
    }

    [RelayCommand]
    private void Exit()
    {
        ExitRequested?.Invoke();
    }

    [RelayCommand]
    private void SwitchToSummary()
    {
        _loadedSettings = _configManager.LoadConfig();
        LoadSummaryFromSettings(_loadedSettings);
        IsModeA = false;
        IsModeB = true;
    }

    [RelayCommand]
    private void SwitchToEdit()
    {
        _loadedSettings = _configManager.LoadConfig();
        BaseUrl = _configManager.LoadBaseUrl();
        BrokerHost = _loadedSettings.BrokerHost;
        BrokerPortText = _loadedSettings.BrokerPort.ToString();
        MachineId = _loadedSettings.MachineId;
        Username = _loadedSettings.Username;
        Password = _loadedSettings.Password;
        UseTls = _loadedSettings.UseTls;
        AllowUntrustedCertificates = _loadedSettings.AllowUntrustedCertificates;
        CaCertificatePath = _loadedSettings.CaCertificatePath;
        RpcTimeoutSecondsText = _loadedSettings.RpcTimeoutSeconds.ToString();
        ReconnectDelaySecondsText = _loadedSettings.ReconnectDelaySeconds.ToString();
        WaveformPublishIntervalMsText = _loadedSettings.WaveformPublishIntervalMs.ToString();

        IsModeA = true;
        IsModeB = false;
        UpdateSaveEnabled();
        StatusMessage = "";
    }

    [RelayCommand]
    private async Task UseExistingAndStart()
    {
        StatusMessage = "正在启动 WebAPI...";
        await StartWebApiAndWait();
    }

    public void SetCaCertificatePath(string path)
    {
        CaCertificatePath = path;
    }

    // ── Process management ──────────────────────────────────

    private async Task StartWebApiAndWait()
    {
        if (_processManager == null)
        {
            // No process manager available (e.g., running without launcher integration)
            // Just notify tab switch so user lands on control tab
            _onReady?.Invoke();
            return;
        }

        _processManager.Start();

        var baseUrl = _configManager.LoadBaseUrl();
        var ready = await _processManager.WaitUntilReadyAsync(baseUrl, 30);

        if (ready)
        {
            StatusMessage = "WebAPI 已启动并就绪。";
            _onReady?.Invoke();
        }
        else
        {
            StatusMessage = "WebAPI 启动超时，请检查 BaseUrl 是否正确";
        }
    }

    // ── Helpers ─────────────────────────────────────────────

    private static int ParseIntOrDefault(string text, int defaultValue)
    {
        return int.TryParse(text, out var value) ? value : defaultValue;
    }

    private MqttSettings BuildSettingsFromForm()
    {
        return new MqttSettings
        {
            BrokerHost = BrokerHost,
            BrokerPort = ParseIntOrDefault(BrokerPortText, 8883),
            MachineId = MachineId,
            Username = Username,
            Password = Password,
            UseTls = UseTls,
            AllowUntrustedCertificates = AllowUntrustedCertificates,
            CaCertificatePath = CaCertificatePath,
            RpcTimeoutSeconds = ParseIntOrDefault(RpcTimeoutSecondsText, 60),
            ReconnectDelaySeconds = ParseIntOrDefault(ReconnectDelaySecondsText, 5),
            WaveformPublishIntervalMs = ParseIntOrDefault(WaveformPublishIntervalMsText, 100)
        };
    }
}
