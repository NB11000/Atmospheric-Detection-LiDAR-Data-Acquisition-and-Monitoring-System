using Microsoft.Extensions.Logging.Abstractions;
using WebAPI.Service;
using Xunit;

namespace WebAPI.Tests;

public class SystemStateServiceTests
{
    [Fact]
    public void UpdateMqttConnectionState_ValueChanges_FiresEvent()
    {
        var service = new SystemStateService(NullLogger<SystemStateService>.Instance);
        var firedCount = 0;
        var lastValue = false;
        service.MqttConnectionStateChanged += v => { firedCount++; lastValue = v; };

        service.UpdateMqttConnectionState(true);

        Assert.Equal(1, firedCount);
        Assert.True(lastValue);

        service.UpdateMqttConnectionState(false);

        Assert.Equal(2, firedCount);
        Assert.False(lastValue);
    }

    [Fact]
    public void UpdateMqttConnectionState_SameValue_DoesNotFire()
    {
        var service = new SystemStateService(NullLogger<SystemStateService>.Instance);
        var firedCount = 0;
        service.MqttConnectionStateChanged += _ => firedCount++;

        service.UpdateMqttConnectionState(false); // 初始 false → false，不变
        Assert.Equal(0, firedCount);

        service.UpdateMqttConnectionState(true);  // false → true，触发
        Assert.Equal(1, firedCount);

        service.UpdateMqttConnectionState(true);  // true → true，不变
        Assert.Equal(1, firedCount);
    }
}
