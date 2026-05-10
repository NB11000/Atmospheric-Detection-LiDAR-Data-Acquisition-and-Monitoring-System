namespace WebAPI.Models;

public static class StateChangeEvents
{
    // 采集卡
    public const string CollectorConnected    = "collector_connected";
    public const string CollectorDisconnected = "collector_disconnected";
    public const string DeviceOpened          = "device_opened";
    public const string DeviceClosed          = "device_closed";
    public const string AcquisitionStarted    = "acquisition_started";
    public const string AcquisitionStopped    = "acquisition_stopped";
    public const string DeviceDisconnected    = "device_disconnected";
    public const string AcquisitionFailed     = "acquisition_failed";
    public const string DeviceOpenFailed      = "device_open_failed";

    // 激光器
    public const string LaserConnected        = "laser_connected";
    public const string LaserDisconnected     = "laser_disconnected";
    public const string LaserOn               = "laser_on";
    public const string LaserOff              = "laser_off";

    // 系统
    public const string Error                 = "error";
    public const string MqttConnected         = "mqtt_connected";
    public const string MqttDisconnected      = "mqtt_disconnected";
}
