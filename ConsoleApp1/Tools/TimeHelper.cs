namespace ConsoleApp1.Helpers;

public static class TimeHelper
{
    /// <summary>
    /// 将采样时钟转换为UTC时间
    /// </summary>
    /// <param name="sampleTick"></param>
    /// <param name="referenceTick"></param>
    /// <param name="referenceUtcTicks"></param>
    /// <param name="frequency"></param>
    /// <returns></returns>
    public static DateTime ToUtcDateTime(
        long sampleTick,
        long referenceTick,
        long referenceUtcTicks,
        long frequency)
    {
        long elapsedTicks = sampleTick - referenceTick;
        long elapsed100ns = (long)((double)elapsedTicks * 10_000_000L / frequency);
        return new DateTime(referenceUtcTicks + elapsed100ns, DateTimeKind.Utc);
    }
}
