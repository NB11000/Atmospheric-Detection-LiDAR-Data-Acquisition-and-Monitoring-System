using ConsoleApp1.Helpers;
using Xunit;

namespace WebAPI.Tests;

public class TimeHelperTests
{
    [Fact]
    public void ToUtcDateTime_SameReferenceTick_ReturnsReferenceTime()
    {
        long refTick = 100_000_000;
        long refUtcTicks = 638_000_000_000_000_000; // some DateTime.UtcNow.Ticks
        long freq = 10_000_000; // typical Stopwatch.Frequency

        DateTime result = TimeHelper.ToUtcDateTime(refTick, refTick, refUtcTicks, freq);

        Assert.Equal(new DateTime(refUtcTicks, DateTimeKind.Utc), result);
    }

    [Fact]
    public void ToUtcDateTime_OneSecondAfterReference_ReturnsReferencePlusOneSecond()
    {
        long refTick = 100_000_000;
        long refUtcTicks = 638_000_000_000_000_000;
        long freq = 10_000_000;

        DateTime result = TimeHelper.ToUtcDateTime(refTick + freq, refTick, refUtcTicks, freq);

        Assert.Equal(new DateTime(refUtcTicks + 10_000_000, DateTimeKind.Utc), result);
    }

    [Fact]
    public void ToUtcDateTime_NegativeOffset_ReturnsReferenceMinusOneSecond()
    {
        long refTick = 100_000_000;
        long refUtcTicks = 638_000_000_000_000_000;
        long freq = 10_000_000;

        DateTime result = TimeHelper.ToUtcDateTime(refTick - freq, refTick, refUtcTicks, freq);

        Assert.Equal(new DateTime(refUtcTicks - 10_000_000, DateTimeKind.Utc), result);
    }

    [Fact]
    public void ToUtcDateTime_LargeSpan_24Hours_ReturnsCorrectTime()
    {
        long refTick = 0;
        long refUtcTicks = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
        long freq = 10_000_000;
        long ticksIn24h = freq * 86400;

        DateTime result = TimeHelper.ToUtcDateTime(refTick + ticksIn24h, refTick, refUtcTicks, freq);

        Assert.Equal(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), result);
    }

    [Fact]
    public void ToUtcDateTime_DifferentFrequency_StillYieldsOneSecond()
    {
        // Linux Stopwatch.Frequency is often 1_000_000_000 (1 GHz)
        long refTick = 500_000_000;
        long refUtcTicks = 638_000_000_000_000_000;
        long freq = 1_000_000_000;

        DateTime result = TimeHelper.ToUtcDateTime(refTick + freq, refTick, refUtcTicks, freq);

        Assert.Equal(new DateTime(refUtcTicks + 10_000_000, DateTimeKind.Utc), result);
    }
}
