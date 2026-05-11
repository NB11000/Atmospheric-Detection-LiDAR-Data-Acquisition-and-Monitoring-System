using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SharedModels;

namespace AvaloniaApplication_ConfigLauncher.ViewModels;

public partial class ControlViewModel : ViewModelBase
{
    private LauncherHttpClient? _httpClient;

    // ── Collector command results ─────────────────────────────

    [ObservableProperty]
    private CommandResult? _openDeviceResult;

    [ObservableProperty]
    private CommandResult? _closeDeviceResult;

    [ObservableProperty]
    private CommandResult? _startAcqResult;

    [ObservableProperty]
    private CommandResult? _stopAcqResult;

    // ── Laser command results ─────────────────────────────────

    [ObservableProperty]
    private CommandResult? _laserConnectResult;

    [ObservableProperty]
    private CommandResult? _laserDisconnectResult;

    [ObservableProperty]
    private CommandResult? _laserOnResult;

    [ObservableProperty]
    private CommandResult? _laserOffResult;

    // ── System state ──────────────────────────────────────────

    [ObservableProperty]
    private SystemStateDto? _currentState;

    [ObservableProperty]
    private bool _isRefreshing;

    // ── Placeholder (kept for backward compat) ────────────────

    [ObservableProperty]
    private string _placeholderText = "WebAPI 未启动";

    public ControlViewModel() : this(null)
    {
    }

    public ControlViewModel(LauncherHttpClient? httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Update the internal HttpClient (e.g., when BaseUrl changes).
    /// </summary>
    public void SetHttpClient(LauncherHttpClient? httpClient)
    {
        _httpClient = httpClient;
    }

    // ── Collector commands ────────────────────────────────────

    [RelayCommand]
    private async Task OpenDevice()
    {
        OpenDeviceResult = _httpClient == null
            ? NoConnectionResult()
            : await _httpClient.OpenDevice();
    }

    [RelayCommand]
    private async Task CloseDevice()
    {
        CloseDeviceResult = _httpClient == null
            ? NoConnectionResult()
            : await _httpClient.CloseDevice();
    }

    [RelayCommand]
    private async Task StartAcquisition()
    {
        StartAcqResult = _httpClient == null
            ? NoConnectionResult()
            : await _httpClient.StartAcquisition();
    }

    [RelayCommand]
    private async Task StopAcquisition()
    {
        StopAcqResult = _httpClient == null
            ? NoConnectionResult()
            : await _httpClient.StopAcquisition();
    }

    // ── Laser commands ────────────────────────────────────────

    [RelayCommand]
    private async Task LaserConnect()
    {
        LaserConnectResult = _httpClient == null
            ? NoConnectionResult()
            : await _httpClient.LaserConnect();
    }

    [RelayCommand]
    private async Task LaserDisconnect()
    {
        LaserDisconnectResult = _httpClient == null
            ? NoConnectionResult()
            : await _httpClient.LaserDisconnect();
    }

    [RelayCommand]
    private async Task LaserOn()
    {
        LaserOnResult = _httpClient == null
            ? NoConnectionResult()
            : await _httpClient.LaserOn();
    }

    [RelayCommand]
    private async Task LaserOff()
    {
        LaserOffResult = _httpClient == null
            ? NoConnectionResult()
            : await _httpClient.LaserOff();
    }

    // ── Refresh state ─────────────────────────────────────────

    [RelayCommand]
    private async Task RefreshState()
    {
        if (_httpClient == null)
        {
            CurrentState = null;
            return;
        }

        IsRefreshing = true;
        try
        {
            CurrentState = await _httpClient.GetSystemState();
            PlaceholderText = "WebAPI 已启动 - 控制面板就绪";
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    // ── Helpers ──────────────────────────────────────────────

    private static CommandResult NoConnectionResult()
    {
        return new CommandResult
        {
            Success = false,
            Code = "CONNECTION_ERROR",
            Message = "无法连接到 WebAPI"
        };
    }
}
