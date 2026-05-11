using WebAPI.Service;
using Xunit;

namespace WebAPI.Tests;

public class PublisherInterfaceTests
{
    [Fact]
    public void MqttEventPublisher_Implements_IMqttEventPublisher()
    {
        Assert.True(typeof(IMqttEventPublisher).IsAssignableFrom(typeof(MqttEventPublisher)));
    }

    [Fact]
    public void SignalRHubPublisher_Implements_ISignalRHubPublisher()
    {
        Assert.True(typeof(ISignalRHubPublisher).IsAssignableFrom(typeof(SignalRHubPublisher)));
    }
}
