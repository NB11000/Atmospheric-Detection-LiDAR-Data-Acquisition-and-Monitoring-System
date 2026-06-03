using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AvaloniaApplication_ConfigLauncher.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ConfigManager _configManager;
    private readonly IWebApiProcessManager _processManager;
    private LauncherHttpClient _httpClient;
    private HttpClient? _reachabilityClient;

    // ── Tab ViewModels ──────────────────────────────────────

    public ConfigViewModel ConfigVm { get; }
    public ControlViewModel ControlVm { get; }
    public LogViewModel LogVm { get; } = new();

    // ── Status bar ──────────────────────────────────────────

    [ObservableProperty]
    private string _baseUrl = "http://localhost:5135";

    [ObservableProperty]
    private string _webApiConnectionStatus = "未连接";

    [ObservableProperty]
    private IBrush _webApiConnectionColor = Brushes.Gray;

    [ObservableProperty]
    private string _mqttConnectionStatus = "—";

    [ObservableProperty]
    private IBrush _mqttConnectionColor = Brushes.Gold;

    // ── Tab selection ───────────────────────────────────────

    [ObservableProperty]
    private int _selectedTabIndex;

    /// <summary>
    /// Issue 04 Part E: True when the WebAPI process is running.
    /// </summary>
    public bool IsWebApiProcessRunning => _processManager.IsProcessRunning;

    public MainWindowViewModel()
    {
        _configManager = new ConfigManager(AppDomain.CurrentDomain.BaseDirectory);
        BaseUrl = _configManager.LoadBaseUrl();

        // Determine WebAPI directory relative to the launcher
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var webApiDir = baseDir; // WebAPI.exe is deployed alongside the launcher
        _processManager = new WebApiProcessManager(webApiDir);

        // Issue 04 Part A: Create LauncherHttpClient with current BaseUrl
        _httpClient = new LauncherHttpClient(BaseUrl);

        // Issue 04 Part B: Create ControlViewModel with HttpClient
        ControlVm = new ControlViewModel(_httpClient);

        ConfigVm = new ConfigViewModel(
            _configManager,
            _processManager,
            onReady: () =>
            {
                // Switch to 控制 tab (index 1)
                SelectedTabIndex = 1;
                ControlVm.PlaceholderText = "WebAPI 已启动 - 控制面板就绪";
                // Refresh state and connection status after switching to control tab
                ControlVm.RefreshStateCommand.Execute(null);
                _ = RefreshConnectionStatusAsync();
            });

        ConfigVm.NotifyBaseUrlChanged = newUrl =>
        {
            BaseUrl = newUrl;
        };

        // Issue 04 Part D: Wire up restart flow callbacks
        ConfigVm.ShowRestartConfirmationAsync = ShowRestartConfirmationAsync;
        ConfigVm.RestartWebApiAsync = RestartWebApiAsync;
        ConfigVm.ExitRequested = () =>
        {
            if (Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow?.Close();
            }
        };

        // Auto-detect: if WebAPI is already running, skip to control tab
        _ = AutoDetectWebApi();
    }

    partial void OnBaseUrlChanged(string value)
    {
        // Recreate LauncherHttpClient when BaseUrl changes
        _httpClient = new LauncherHttpClient(value);
        ControlVm.SetHttpClient(_httpClient);
        _ = RefreshConnectionStatusAsync();
    }

    public void SaveBaseUrl()
    {
        _configManager.SaveBaseUrl(BaseUrl);
        // OnBaseUrlChanged is called automatically by the source generator
        // when BaseUrl changes, but save triggers if user just unfocuses
        // without changing value. Recreate to be safe.
        _httpClient = new LauncherHttpClient(BaseUrl);
        ControlVm.SetHttpClient(_httpClient);
    }


    // ── Connection monitoring ──────────────────────────────────

    /// <summary>
    /// Unified connection status refresh: HTTP reachability + MQTT state.
    /// Called on startup, BaseUrl change, and manual refresh.
    /// </summary>
    public async Task RefreshConnectionStatusAsync()
    {
        var isReachable = await IsWebApiHttpReachableAsync();

        if (isReachable)
        {
            WebApiConnectionStatus = "已连接";
            WebApiConnectionColor = Brushes.Green;

            try
            {
                var state = await _httpClient.GetSystemState();
                if (state.Server.IsMqttConnected)
                {
                    MqttConnectionStatus = "已连接";
                    MqttConnectionColor = Brushes.Green;
                }
                else
                {
                    MqttConnectionStatus = "未连接";
                    MqttConnectionColor = Brushes.Gray;
                }
            }
            catch
            {
                MqttConnectionStatus = "—";
                MqttConnectionColor = Brushes.Gold;
            }
        }
        else
        {
            WebApiConnectionStatus = "未连接";
            WebApiConnectionColor = Brushes.Gray;
            MqttConnectionStatus = "—";
            MqttConnectionColor = Brushes.Gold;
        }
    }

    // ── Auto-detection ──────────────────────────────────────────

    private async Task AutoDetectWebApi()
    {
        await Task.Delay(200);
        await RefreshConnectionStatusAsync();

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var response = await client.GetAsync(BaseUrl + "/");
            if (response.IsSuccessStatusCode)
            {
                SelectedTabIndex = 1;
                ControlVm.PlaceholderText = "WebAPI 已启动 - 控制面板就绪";
                ControlVm.RefreshStateCommand.Execute(null);
            }
        }
        catch
        {
        }
    }

    // ── Issue 04 Part D: Restart flow implementation ────────────

    /// <summary>
    /// Shows the restart confirmation dialog.
    /// Must be called on the UI thread via Dispatcher.
    /// </summary>
    private async Task<bool> ShowRestartConfirmationAsync()
    {
        var tcs = new TaskCompletionSource<bool>();

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                // Get the main window to use as owner
                var window = Avalonia.Application.Current?.ApplicationLifetime
                    is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null;

                if (window == null)
                {
                    tcs.TrySetResult(false);
                    return;
                }

                var result = await ShowConfirmationDialogAsync(
                    window,
                    "配置已保存",
                    "配置已保存，WebAPI 需重启生效，是否立即重启？");

                tcs.TrySetResult(result);
            }
            catch
            {
                tcs.TrySetResult(false);
            }
        });

        return await tcs.Task;
    }

    private async Task<bool> RestartWebApiAsync()
    {
        try
        {
            // 1. Gracefully shut down WebAPI via HTTP
            await _httpClient.ShutdownWebApi();

            // 2. Stop the process (with fallback to kill)
            await _processManager.StopAsync(BaseUrl, 5);

            // 3. Start a new process
            _processManager.Start();

            // 4. Wait until ready
            var ready = await _processManager.WaitUntilReadyAsync(BaseUrl, 30);

            if (ready)
            {
                // 5. Switch to Control tab and refresh state
                SelectedTabIndex = 1;
                ControlVm.PlaceholderText = "WebAPI 已启动 - 控制面板就绪";
                ControlVm.RefreshStateCommand.Execute(null);
                _ = RefreshConnectionStatusAsync();
            }

            return ready;
        }
        catch
        {
            return false;
        }
    }

    internal MainWindowViewModel(
        ConfigManager configManager,
        IWebApiProcessManager processManager,
        LauncherHttpClient httpClient,
        HttpClient? reachabilityClient = null)
    {
        _configManager = configManager;
        BaseUrl = configManager.LoadBaseUrl();
        _processManager = processManager;
        _httpClient = httpClient;
        _reachabilityClient = reachabilityClient;

        ControlVm = new ControlViewModel(_httpClient);
        ConfigVm = new ConfigViewModel(_configManager, _processManager, onReady: () => { });
        ConfigVm.NotifyBaseUrlChanged = newUrl =>
        {
            BaseUrl = newUrl;
        };
    }

    public async Task<bool> ShutdownWebApiAsync()
    {
        try
        {
            var shutdownOk = await _httpClient.ShutdownWebApi();
            if (!shutdownOk)
                return false;
            return await _processManager.StopAsync(BaseUrl, 3);
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> IsWebApiHttpReachableAsync()
    {
        try
        {
            var client = _reachabilityClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            var response = await client.GetAsync(BaseUrl + "/");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static Task<bool> ShowConfirmationDialogAsync(
        Avalonia.Controls.Window owner, string title, string message)
    {
        var tcs = new TaskCompletionSource<bool>();

        var textBlock = new Avalonia.Controls.TextBlock
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(15, 15, 15, 10)
        };

        var yesButton = new Avalonia.Controls.Button
        {
            Content = "是",
            Width = 70,
            Margin = new Avalonia.Thickness(5, 0, 5, 0)
        };

        var noButton = new Avalonia.Controls.Button
        {
            Content = "否",
            Width = 70,
            Margin = new Avalonia.Thickness(5, 0, 5, 0)
        };

        var buttonPanel = new Avalonia.Controls.StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Margin = new Avalonia.Thickness(0, 5, 0, 15),
            Children = { yesButton, noButton }
        };

        var panel = new Avalonia.Controls.StackPanel
        {
            Children = { textBlock, buttonPanel }
        };

        var dialog = new Avalonia.Controls.Window
        {
            Title = title,
            Width = 400,
            Height = 150,
            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner,
            Content = panel,
            CanResize = false
        };

        yesButton.Click += (_, _) => { tcs.TrySetResult(true); dialog.Close(); };
        noButton.Click += (_, _) => { tcs.TrySetResult(false); dialog.Close(); };

        dialog.ShowDialog(owner);
        return tcs.Task;
    }
}
