namespace WebAPI.Models
{
    /// <summary>
    /// 激光雷达反演算法配置参数（从 DeviceConfig.json 的 LidarAlgorithm 节读取）
    /// </summary>
    public class LidarAlgorithmConfig
    {
        /// <summary>双通道增益均衡系数（CH2 *= 该值，仅双通道模式生效）</summary>
        public double GainEqualizationCoefficient { get; set; } = 1.0;

        /// <summary>Cn² 球面波公式 K 常数，默认 4.48</summary>
        public double KConstant { get; set; } = 4.48;

        /// <summary>接收孔径 (m)</summary>
        public double ReceiverApertureD_m { get; set; } = 0.2;

        /// <summary>路径长度 (m)</summary>
        public double PathLengthL_m { get; set; } = 1000.0;

        /// <summary>Cn² 滑动窗口帧数，默认 100</summary>
        public int Cn2WindowFrames { get; set; } = 100;

        /// <summary>Fernald 远端边界距离 (m)</summary>
        public double FernaldBoundaryDistance_m { get; set; } = 3000.0;

        /// <summary>激光波长 (nm)</summary>
        public double LaserWavelength_nm { get; set; } = 532.0;

        /// <summary>Angstrom 指数，默认 1.3</summary>
        public double AngstromExponent { get; set; } = 1.3;

        /// <summary>暗电流采样点数（0 表示不扣除）</summary>
        public int DarkCurrentSampleCount { get; set; } = 0;

        /// <summary>采样率 (Hz)，用于距离门宽度计算</summary>
        public double SampleRateHz { get; set; } = 20_000_000.0;

        /// <summary>近场盲区距离 (m)，盲区内电压置零</summary>
        public double BlindZoneDistance_m { get; set; } = 30.0;
    }
}
