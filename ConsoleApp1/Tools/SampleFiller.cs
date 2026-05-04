using System;
using ConsoleApp1.Models;

namespace ConsoleApp1.Helpers;

public static class SampleFiller
{
    /// <summary>
    /// 计算每个采样点对应的时钟周期数
    /// </summary>
    /// <param name="frequency"></param>
    /// <param name="sampleRateKhz"></param>
    /// <returns></returns>
    public static long ComputeTicksPerSample(long frequency, int sampleRateKhz)
    {
        double d = (double)frequency / (double)(sampleRateKhz * 1000);
        return (long)Math.Round(d);
    }

    /// <summary>
    /// 填充一个采样点的数据
    /// </summary>
    /// <param name="sample"></param>
    /// <param name="indexInFrame"></param>
    /// <param name="baseTick"></param>
    /// <param name="ticksPerSample"></param>
    /// <param name="globalSampleIndex"></param>
    /// <param name="voltage1"></param>
    /// <param name="voltage2"></param>
    public static void FillOne(ref StructuredSample sample,
        int indexInFrame, long baseTick, long ticksPerSample,
        ref long globalSampleIndex, double[]? voltage1, double[]? voltage2)
    {
        sample.Timestamp = globalSampleIndex++;
        sample.Time = baseTick + indexInFrame * ticksPerSample;
        sample.CH1 = voltage1?[indexInFrame] ?? 0;
        sample.CH2 = voltage2?[indexInFrame] ?? 0;
        sample.Vis = 0.0;
        sample.Cn2 = 0.0;
        sample.Temp = 0.0;
        sample.Humi = 0.0;
        sample.Press = 0.0;
        sample.WindSpd = 0.0;
        sample.Rain = 0.0;
        sample.WindDir = 0.0;
    }
}
