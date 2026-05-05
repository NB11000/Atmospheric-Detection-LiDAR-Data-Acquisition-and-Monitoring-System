using System.Reflection;
using ConsoleApp1.Models;
using ConsoleApp1.Service;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedMemoryFramework;
using Xunit;

namespace WebAPI.Tests;

public class AD_ControlcsDITests
{
    [Fact]
    public void ServiceProvider_ResolvesAD_Controlcs_WhenAllDependenciesRegistered()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILogger>(NullLogger.Instance);
        services.AddSingleton(new CaptureCardConfig());
        services.AddSingleton(new UISharedBuffer());
        services.AddSingleton(new CoreDataBus());
        services.AddSingleton<AD_Controlcs>();

        var provider = services.BuildServiceProvider();
        var controller = provider.GetRequiredService<AD_Controlcs>();

        Assert.NotNull(controller);
        Assert.Equal("采集卡未打开", controller.LastStatusMessage);
    }

    [Fact]
    public void AD_Controlcs_UsesInjectedCoreDataBus_SameInstance()
    {
        var coreBus = new CoreDataBus();

        var services = new ServiceCollection();
        services.AddSingleton<ILogger>(NullLogger.Instance);
        services.AddSingleton(new CaptureCardConfig());
        services.AddSingleton(new UISharedBuffer());
        services.AddSingleton(coreBus);
        services.AddSingleton<AD_Controlcs>();

        var provider = services.BuildServiceProvider();
        var controller = provider.GetRequiredService<AD_Controlcs>();

        var field = typeof(AD_Controlcs)
            .GetField("_coreBus", BindingFlags.NonPublic | BindingFlags.Instance);
        var injected = field!.GetValue(controller);

        Assert.Same(coreBus, injected);
    }

    [Fact]
    public void AD_Controlcs_UsesInjectedUISharedBuffer_SameInstance()
    {
        var uiBuffer = new UISharedBuffer();

        var services = new ServiceCollection();
        services.AddSingleton<ILogger>(NullLogger.Instance);
        services.AddSingleton(new CaptureCardConfig());
        services.AddSingleton(uiBuffer);
        services.AddSingleton(new CoreDataBus());
        services.AddSingleton<AD_Controlcs>();

        var provider = services.BuildServiceProvider();
        var controller = provider.GetRequiredService<AD_Controlcs>();

        var field = typeof(AD_Controlcs)
            .GetField("_uISharedBuffer", BindingFlags.NonPublic | BindingFlags.Instance);
        var injected = field!.GetValue(controller);

        Assert.Same(uiBuffer, injected);
    }

    [Fact]
    public void AD_Controlcs_UsesInjectedCaptureCardConfig_SameInstance()
    {
        var config = new CaptureCardConfig();

        var services = new ServiceCollection();
        services.AddSingleton<ILogger>(NullLogger.Instance);
        services.AddSingleton(config);
        services.AddSingleton(new UISharedBuffer());
        services.AddSingleton(new CoreDataBus());
        services.AddSingleton<AD_Controlcs>();

        var provider = services.BuildServiceProvider();
        var controller = provider.GetRequiredService<AD_Controlcs>();

        var field = typeof(AD_Controlcs)
            .GetField("_deviceConfig", BindingFlags.NonPublic | BindingFlags.Instance);
        var injected = field!.GetValue(controller);

        Assert.Same(config, injected);
    }

    [Fact]
    public void AD_Controlcs_UsesInjectedILogger_SameInstance()
    {
        var logger = NullLogger.Instance;

        var services = new ServiceCollection();
        services.AddSingleton<ILogger>(logger);
        services.AddSingleton(new CaptureCardConfig());
        services.AddSingleton(new UISharedBuffer());
        services.AddSingleton(new CoreDataBus());
        services.AddSingleton<AD_Controlcs>();

        var provider = services.BuildServiceProvider();
        var controller = provider.GetRequiredService<AD_Controlcs>();

        var field = typeof(AD_Controlcs)
            .GetField("_logger", BindingFlags.NonPublic | BindingFlags.Instance);
        var injected = field!.GetValue(controller);

        Assert.Same(logger, injected);
    }
}
