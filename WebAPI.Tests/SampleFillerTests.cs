using ConsoleApp1.Helpers;
using ConsoleApp1.Models;
using Xunit;

namespace WebAPI.Tests;

public class SampleFillerTests
{
    [Fact]
    public void ComputeTicksPerSample_TypicalFrequency_ReturnsCorrectValue()
    {
        long result = SampleFiller.ComputeTicksPerSample(
            frequency: 10_000_000,
            sampleRateKhz: 1000);

        Assert.Equal(10L, result);
    }

    [Fact]
    public void FillOne_SetsTimeCorrectly()
    {
        long globalIndex = 0;
        var sample = default(StructuredSample);

        SampleFiller.FillOne(ref sample,
            indexInFrame: 3,
            baseTick: 1000L,
            ticksPerSample: 10L,
            globalSampleIndex: ref globalIndex,
            voltage1: null,
            voltage2: null);

        Assert.Equal(1000L + 3 * 10L, sample.Time);
    }

    [Fact]
    public void FillOne_IncrementsTimestampAcrossCalls()
    {
        long globalIndex = 0;
        var s = default(StructuredSample);

        SampleFiller.FillOne(ref s, 0, 0, 10, ref globalIndex, null, null);
        Assert.Equal(0L, s.Timestamp);
        Assert.Equal(1L, globalIndex);

        SampleFiller.FillOne(ref s, 0, 0, 10, ref globalIndex, null, null);
        Assert.Equal(1L, s.Timestamp);
        Assert.Equal(2L, globalIndex);

        SampleFiller.FillOne(ref s, 0, 0, 10, ref globalIndex, null, null);
        Assert.Equal(2L, s.Timestamp);
        Assert.Equal(3L, globalIndex);
    }

    [Fact]
    public void FillOne_CopiesCH1CH2FromArrays()
    {
        long globalIndex = 0;
        var s = default(StructuredSample);
        double[] v1 = { 1.1, 2.2, 3.3 };
        double[] v2 = { 4.4, 5.5, 6.6 };

        SampleFiller.FillOne(ref s, indexInFrame: 1, baseTick: 0, ticksPerSample: 10,
            globalSampleIndex: ref globalIndex, voltage1: v1, voltage2: v2);

        Assert.Equal(2.2, s.CH1);
        Assert.Equal(5.5, s.CH2);
    }

    [Fact]
    public void FillOne_NullChannels_SetToZero()
    {
        long globalIndex = 0;
        var s = default(StructuredSample);

        SampleFiller.FillOne(ref s, indexInFrame: 0, baseTick: 0, ticksPerSample: 10,
            globalSampleIndex: ref globalIndex, voltage1: null, voltage2: null);

        Assert.Equal(0.0, s.CH1);
        Assert.Equal(0.0, s.CH2);
    }

    [Fact]
    public void FillOne_SetsPlaceholderFieldsToZero()
    {
        long globalIndex = 0;
        var s = default(StructuredSample);
        // Pre-fill with non-zero to verify overwrite
        s.Vis = 99; s.Cn2 = 99; s.Temp = 99; s.Humi = 99;
        s.Press = 99; s.WindSpd = 99; s.Rain = 99; s.WindDir = 99;

        SampleFiller.FillOne(ref s, indexInFrame: 0, baseTick: 0, ticksPerSample: 10,
            globalSampleIndex: ref globalIndex, voltage1: null, voltage2: null);

        Assert.Equal(0.0, s.Vis);
        Assert.Equal(0.0, s.Cn2);
        Assert.Equal(0.0, s.Temp);
        Assert.Equal(0.0, s.Humi);
        Assert.Equal(0.0, s.Press);
        Assert.Equal(0.0, s.WindSpd);
        Assert.Equal(0.0, s.Rain);
        Assert.Equal(0.0, s.WindDir);
    }
}
