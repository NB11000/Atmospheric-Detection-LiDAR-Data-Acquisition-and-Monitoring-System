using System.Runtime.InteropServices;

namespace ConsoleApp1.Models
{
    [StructLayout(LayoutKind.Sequential)]
    public struct StructuredSample
    {
        public long Timestamp;   // 采样点序号ID（全局递增）
        public long Time;        // 物理采集时间戳（Stopwatch.GetTimestamp()，单位=Stopwatch.Frequency ticks）
        public double CH1;       // 通道1原始电压值
        public double CH2;       // 通道2原始电压值
        public double Vis;       // 能见度 (m)，暂占位 0.0
        public double Cn2;       // 大气折射率结构常数 (m⁻²/³)，暂占位 0.0

        // 未来扩展数据
        public double Temp;      // 温度 (°C)
        public double Humi;      // 湿度 (%)
        public double Press;     // 气压 (hPa)
        public double WindSpd;   // 风速 (m/s)
        public double Rain;      // 降雨量 (mm)
        public double WindDir;   // 风向 (°)
    }
    // 总大小：12 × 8 = 96 bytes
}
