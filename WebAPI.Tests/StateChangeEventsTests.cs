using WebAPI.Models;
using Xunit;

namespace WebAPI.Tests;

public class StateChangeEventsTests
{
    [Fact]
    public void CollectorConstants_HaveExpectedValues()
    {
        Assert.Equal("collector_connected", StateChangeEvents.CollectorConnected);
        Assert.Equal("collector_disconnected", StateChangeEvents.CollectorDisconnected);
        Assert.Equal("device_opened", StateChangeEvents.DeviceOpened);
        Assert.Equal("device_closed", StateChangeEvents.DeviceClosed);
        Assert.Equal("acquisition_started", StateChangeEvents.AcquisitionStarted);
        Assert.Equal("acquisition_stopped", StateChangeEvents.AcquisitionStopped);
        Assert.Equal("device_disconnected", StateChangeEvents.DeviceDisconnected);
        Assert.Equal("acquisition_failed", StateChangeEvents.AcquisitionFailed);
        Assert.Equal("device_open_failed", StateChangeEvents.DeviceOpenFailed);
    }

    [Fact]
    public void LaserConstants_HaveExpectedValues()
    {
        Assert.Equal("laser_connected", StateChangeEvents.LaserConnected);
        Assert.Equal("laser_disconnected", StateChangeEvents.LaserDisconnected);
        Assert.Equal("laser_on", StateChangeEvents.LaserOn);
        Assert.Equal("laser_off", StateChangeEvents.LaserOff);
    }

    [Fact]
    public void SystemConstants_HaveExpectedValues()
    {
        Assert.Equal("error", StateChangeEvents.Error);
        Assert.Equal("mqtt_connected", StateChangeEvents.MqttConnected);
        Assert.Equal("mqtt_disconnected", StateChangeEvents.MqttDisconnected);
    }
}
