using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SharedModels;

namespace AvaloniaApplication_ConfigLauncher.ViewModels;

public partial class DeviceConfigViewModel : ViewModelBase
{
    private LauncherHttpClient _httpClient;
    private CancellationTokenSource? _cts;

    public DeviceConfigViewModel(LauncherHttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public void SetHttpClient(LauncherHttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    [ObservableProperty]
    private int _selectedSubTabIndex;

    partial void OnSelectedSubTabIndexChanged(int value)
    {
        CancelPendingHttpCall();
        _ = LoadConfig();
    }

    [ObservableProperty]
    private bool _isWebApiConnected;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "";

    // ── Capture Card ─────────────────────────────────────────

    [ObservableProperty]
    private int _captureDeviceId = 0;

    [ObservableProperty]
    private int _captureSyncChannelIndex = 2;

    [ObservableProperty]
    private string _captureSampleRate = "1000";

    [ObservableProperty]
    private int _captureClockSourceIndex = 0;

    [ObservableProperty]
    private int _captureHalfFullThreshold = 5;

    [ObservableProperty]
    private int _captureTriggerSourceIndex = 1;

    [ObservableProperty]
    private int _captureRangeIndex = 0;

    // ── Radar ────────────────────────────────────────────────

    [ObservableProperty]
    private string _radarLaserPower = "0";

    [ObservableProperty]
    private string _radarLaserModulationFrequency = "0";

    [ObservableProperty]
    private int _radarSerialPortIndex = 0;

    [ObservableProperty]
    private int _radarBaudRateIndex = 0;

    private static readonly string[] _serialPorts = { "COM1", "COM2", "COM3", "COM4", "COM5", "COM6" };
    private static readonly int[] _baudRates = { 9600, 19200, 38400, 57600, 115200 };

    // ── LiDAR ────────────────────────────────────────────────

    [ObservableProperty]
    private string _lidarGainEqualizationCoefficient = "1.0";

    [ObservableProperty]
    private string _lidarKConstant = "4.48";

    [ObservableProperty]
    private string _lidarReceiverApertureD_m = "0.2";

    [ObservableProperty]
    private string _lidarPathLengthL_m = "1000.0";

    [ObservableProperty]
    private string _lidarCn2WindowFrames = "100";

    [ObservableProperty]
    private string _lidarFernaldBoundaryDistance_m = "3000.0";

    [ObservableProperty]
    private string _lidarLaserWavelength_nm = "532.0";

    [ObservableProperty]
    private string _lidarAngstromExponent = "1.3";

    [ObservableProperty]
    private string _lidarDarkCurrentSampleCount = "0";

    [ObservableProperty]
    private string _lidarSampleRateHz = "20000000.0";

    [ObservableProperty]
    private string _lidarBlindZoneDistance_m = "30.0";

    // ── Persistence ──────────────────────────────────────────

    [ObservableProperty]
    private string _persistenceDataDirectory = "data";

    // ── Commands ─────────────────────────────────────────────

    [RelayCommand]
    private async Task Save()
    {
        if (IsBusy) return;

        IsBusy = true;
        StatusMessage = "";
        _cts = new CancellationTokenSource();

        try
        {
            var token = _cts.Token;
            token.ThrowIfCancellationRequested();

            bool success = SelectedSubTabIndex switch
            {
                0 => await SaveCaptureCard(token),
                1 => await SaveRadar(token),
                2 => await SaveLidar(token),
                3 => await SavePersistence(token),
                _ => false
            };

            if (success)
                StatusMessage = "已保存";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private async Task Refresh()
    {
        if (IsBusy) return;

        CancelPendingHttpCall();
        await LoadConfig();
    }

    [RelayCommand]
    private async Task ResetDefault()
    {
        if (IsBusy) return;

        IsBusy = true;
        StatusMessage = "";
        _cts = new CancellationTokenSource();

        try
        {
            var token = _cts.Token;
            token.ThrowIfCancellationRequested();

            switch (SelectedSubTabIndex)
            {
                case 0: await LoadDefaultCaptureCard(token); break;
                case 1: await LoadDefaultRadar(token); break;
                case 2: await LoadDefaultLidar(token); break;
                case 3: await LoadDefaultPersistence(token); break;
            }

            StatusMessage = "已恢复默认值";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            StatusMessage = $"恢复默认失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    // ── LoadConfig ───────────────────────────────────────────

    private async Task LoadConfig()
    {
        IsBusy = true;
        StatusMessage = "";
        _cts = new CancellationTokenSource();

        try
        {
            var token = _cts.Token;
            token.ThrowIfCancellationRequested();

            bool connected = SelectedSubTabIndex switch
            {
                0 => await LoadCaptureCard(token),
                1 => await LoadRadar(token),
                2 => await LoadLidar(token),
                3 => await LoadPersistence(token),
                _ => false
            };

            IsWebApiConnected = connected;
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            IsWebApiConnected = false;
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    // ── Cancel ───────────────────────────────────────────────

    private void CancelPendingHttpCall()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }
    }

    // ── Capture Card helpers ─────────────────────────────────

    private async Task<bool> LoadCaptureCard(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var config = await _httpClient.GetCaptureCardConfig();
        token.ThrowIfCancellationRequested();
        if (config == null) return false;

        CaptureDeviceId = config.DeviceId;
        CaptureSyncChannelIndex = config.SyncChannelIndex;
        CaptureSampleRate = config.SampleRate.ToString();
        CaptureClockSourceIndex = config.ClockSourceIndex;
        CaptureHalfFullThreshold = config.HalfFullThreshold;
        CaptureTriggerSourceIndex = config.TriggerSourceIndex;
        CaptureRangeIndex = config.RangeIndex;
        return true;
    }

    private async Task<bool> SaveCaptureCard(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var config = new CaptureCardConfig
        {
            DeviceId = CaptureDeviceId,
            SyncChannelIndex = CaptureSyncChannelIndex,
            SampleRate = ParseDecimalOrDefault(CaptureSampleRate, 1000),
            ClockSourceIndex = CaptureClockSourceIndex,
            HalfFullThreshold = CaptureHalfFullThreshold,
            TriggerSourceIndex = CaptureTriggerSourceIndex,
            RangeIndex = CaptureRangeIndex
        };

        token.ThrowIfCancellationRequested();
        var result = await _httpClient.UpdateCaptureCardConfig(config);
        token.ThrowIfCancellationRequested();
        if (result == null) return false;

        CaptureDeviceId = result.DeviceId;
        CaptureSyncChannelIndex = result.SyncChannelIndex;
        CaptureSampleRate = result.SampleRate.ToString();
        CaptureClockSourceIndex = result.ClockSourceIndex;
        CaptureHalfFullThreshold = result.HalfFullThreshold;
        CaptureTriggerSourceIndex = result.TriggerSourceIndex;
        CaptureRangeIndex = result.RangeIndex;
        return true;
    }

    private async Task LoadDefaultCaptureCard(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var config = await _httpClient.GetDefaultCaptureCardConfig();
        token.ThrowIfCancellationRequested();
        if (config == null)
        {
            StatusMessage = "无法获取默认配置";
            return;
        }

        CaptureDeviceId = config.DeviceId;
        CaptureSyncChannelIndex = config.SyncChannelIndex;
        CaptureSampleRate = config.SampleRate.ToString();
        CaptureClockSourceIndex = config.ClockSourceIndex;
        CaptureHalfFullThreshold = config.HalfFullThreshold;
        CaptureTriggerSourceIndex = config.TriggerSourceIndex;
        CaptureRangeIndex = config.RangeIndex;
    }

    // ── Radar helpers ────────────────────────────────────────

    private async Task<bool> LoadRadar(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var config = await _httpClient.GetRadarConfig();
        token.ThrowIfCancellationRequested();
        if (config == null) return false;

        RadarLaserPower = config.LaserPower.ToString();
        RadarLaserModulationFrequency = config.LaserModulationFrequency.ToString();
        RadarSerialPortIndex = Math.Clamp(Array.IndexOf(_serialPorts, config.SerialPort), 0, _serialPorts.Length - 1);
        RadarBaudRateIndex = Math.Clamp(Array.IndexOf(_baudRates, config.BaudRate), 0, _baudRates.Length - 1);
        return true;
    }

    private async Task<bool> SaveRadar(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var config = new RadarConfig
        {
            LaserPower = ParseIntOrDefault(RadarLaserPower, 0),
            LaserModulationFrequency = ParseIntOrDefault(RadarLaserModulationFrequency, 0),
            SerialPort = _serialPorts[Math.Clamp(RadarSerialPortIndex, 0, _serialPorts.Length - 1)],
            BaudRate = _baudRates[Math.Clamp(RadarBaudRateIndex, 0, _baudRates.Length - 1)]
        };

        token.ThrowIfCancellationRequested();
        var result = await _httpClient.UpdateRadarConfig(config);
        token.ThrowIfCancellationRequested();
        if (result == null) return false;

        RadarLaserPower = result.LaserPower.ToString();
        RadarLaserModulationFrequency = result.LaserModulationFrequency.ToString();
        RadarSerialPortIndex = Math.Clamp(Array.IndexOf(_serialPorts, result.SerialPort), 0, _serialPorts.Length - 1);
        RadarBaudRateIndex = Math.Clamp(Array.IndexOf(_baudRates, result.BaudRate), 0, _baudRates.Length - 1);
        return true;
    }

    private async Task LoadDefaultRadar(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var config = await _httpClient.GetDefaultRadarConfig();
        token.ThrowIfCancellationRequested();
        if (config == null)
        {
            StatusMessage = "无法获取默认配置";
            return;
        }

        RadarLaserPower = config.LaserPower.ToString();
        RadarLaserModulationFrequency = config.LaserModulationFrequency.ToString();
        RadarSerialPortIndex = Math.Clamp(Array.IndexOf(_serialPorts, config.SerialPort), 0, _serialPorts.Length - 1);
        RadarBaudRateIndex = Math.Clamp(Array.IndexOf(_baudRates, config.BaudRate), 0, _baudRates.Length - 1);
    }

    // ── LiDAR helpers ────────────────────────────────────────

    private async Task<bool> LoadLidar(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var config = await _httpClient.GetLidarConfig();
        token.ThrowIfCancellationRequested();
        if (config == null) return false;

        LidarGainEqualizationCoefficient = config.GainEqualizationCoefficient.ToString();
        LidarKConstant = config.KConstant.ToString();
        LidarReceiverApertureD_m = config.ReceiverApertureD_m.ToString();
        LidarPathLengthL_m = config.PathLengthL_m.ToString();
        LidarCn2WindowFrames = config.Cn2WindowFrames.ToString();
        LidarFernaldBoundaryDistance_m = config.FernaldBoundaryDistance_m.ToString();
        LidarLaserWavelength_nm = config.LaserWavelength_nm.ToString();
        LidarAngstromExponent = config.AngstromExponent.ToString();
        LidarDarkCurrentSampleCount = config.DarkCurrentSampleCount.ToString();
        LidarSampleRateHz = config.SampleRateHz.ToString();
        LidarBlindZoneDistance_m = config.BlindZoneDistance_m.ToString();
        return true;
    }

    private async Task<bool> SaveLidar(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var config = new LidarAlgorithmConfig
        {
            GainEqualizationCoefficient = ParseDoubleOrDefault(LidarGainEqualizationCoefficient, 1.0),
            KConstant = ParseDoubleOrDefault(LidarKConstant, 4.48),
            ReceiverApertureD_m = ParseDoubleOrDefault(LidarReceiverApertureD_m, 0.2),
            PathLengthL_m = ParseDoubleOrDefault(LidarPathLengthL_m, 1000.0),
            Cn2WindowFrames = ParseIntOrDefault(LidarCn2WindowFrames, 100),
            FernaldBoundaryDistance_m = ParseDoubleOrDefault(LidarFernaldBoundaryDistance_m, 3000.0),
            LaserWavelength_nm = ParseDoubleOrDefault(LidarLaserWavelength_nm, 532.0),
            AngstromExponent = ParseDoubleOrDefault(LidarAngstromExponent, 1.3),
            DarkCurrentSampleCount = ParseIntOrDefault(LidarDarkCurrentSampleCount, 0),
            SampleRateHz = ParseDoubleOrDefault(LidarSampleRateHz, 20_000_000.0),
            BlindZoneDistance_m = ParseDoubleOrDefault(LidarBlindZoneDistance_m, 30.0)
        };

        token.ThrowIfCancellationRequested();
        var result = await _httpClient.UpdateLidarConfig(config);
        token.ThrowIfCancellationRequested();
        if (result == null) return false;

        LidarGainEqualizationCoefficient = result.GainEqualizationCoefficient.ToString();
        LidarKConstant = result.KConstant.ToString();
        LidarReceiverApertureD_m = result.ReceiverApertureD_m.ToString();
        LidarPathLengthL_m = result.PathLengthL_m.ToString();
        LidarCn2WindowFrames = result.Cn2WindowFrames.ToString();
        LidarFernaldBoundaryDistance_m = result.FernaldBoundaryDistance_m.ToString();
        LidarLaserWavelength_nm = result.LaserWavelength_nm.ToString();
        LidarAngstromExponent = result.AngstromExponent.ToString();
        LidarDarkCurrentSampleCount = result.DarkCurrentSampleCount.ToString();
        LidarSampleRateHz = result.SampleRateHz.ToString();
        LidarBlindZoneDistance_m = result.BlindZoneDistance_m.ToString();
        return true;
    }

    private async Task LoadDefaultLidar(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var config = await _httpClient.GetDefaultLidarConfig();
        token.ThrowIfCancellationRequested();
        if (config == null)
        {
            StatusMessage = "无法获取默认配置";
            return;
        }

        LidarGainEqualizationCoefficient = config.GainEqualizationCoefficient.ToString();
        LidarKConstant = config.KConstant.ToString();
        LidarReceiverApertureD_m = config.ReceiverApertureD_m.ToString();
        LidarPathLengthL_m = config.PathLengthL_m.ToString();
        LidarCn2WindowFrames = config.Cn2WindowFrames.ToString();
        LidarFernaldBoundaryDistance_m = config.FernaldBoundaryDistance_m.ToString();
        LidarLaserWavelength_nm = config.LaserWavelength_nm.ToString();
        LidarAngstromExponent = config.AngstromExponent.ToString();
        LidarDarkCurrentSampleCount = config.DarkCurrentSampleCount.ToString();
        LidarSampleRateHz = config.SampleRateHz.ToString();
        LidarBlindZoneDistance_m = config.BlindZoneDistance_m.ToString();
    }

    // ── Persistence helpers ──────────────────────────────────

    private async Task<bool> LoadPersistence(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var config = await _httpClient.GetPersistenceConfig();
        token.ThrowIfCancellationRequested();
        if (config == null) return false;

        PersistenceDataDirectory = config.DataDirectory;
        return true;
    }

    private async Task<bool> SavePersistence(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        if (!System.IO.Path.IsPathRooted(PersistenceDataDirectory))
        {
            StatusMessage = "数据目录必须是绝对路径（如 D:\\Data）";
            return false;
        }

        var config = new PersistenceSettings
        {
            DataDirectory = PersistenceDataDirectory
        };

        token.ThrowIfCancellationRequested();
        var result = await _httpClient.UpdatePersistenceConfig(config);
        token.ThrowIfCancellationRequested();
        if (result == null) return false;

        PersistenceDataDirectory = result.DataDirectory;
        return true;
    }

    private async Task LoadDefaultPersistence(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var config = await _httpClient.GetDefaultPersistenceConfig();
        token.ThrowIfCancellationRequested();
        if (config == null)
        {
            StatusMessage = "无法获取默认配置";
            return;
        }

        PersistenceDataDirectory = config.DataDirectory;
    }

    // ── Parse helpers ────────────────────────────────────────

    private static int ParseIntOrDefault(string text, int defaultValue)
    {
        return int.TryParse(text, out var value) ? value : defaultValue;
    }

    private static double ParseDoubleOrDefault(string text, double defaultValue)
    {
        return double.TryParse(text, out var value) ? value : defaultValue;
    }

    private static decimal ParseDecimalOrDefault(string text, decimal defaultValue)
    {
        return decimal.TryParse(text, out var value) ? value : defaultValue;
    }
}
