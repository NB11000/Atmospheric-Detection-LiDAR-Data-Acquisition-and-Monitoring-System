using System;
using AvaloniaApplication1.Models;
using ConsoleApp1.Models;

namespace ConsoleApp1.Service
{
    /// <summary>
    /// 激光雷达核心反演器：预处理 → Fernald 能见度 → 闪烁方差 Cn²
    /// </summary>
    public class LidarInverter
    {
        private readonly LidarAlgorithmConfig _config;
        private int _frameCount;

        // Cn² 滑动窗口环形缓冲区（仅双通道模式使用）
        private readonly double[][] _ch1History;
        private readonly double[][] _ch2History;
        private int _ringIndex;

        private const double C = 299_792_458.0; // 真空光速 (m/s)

        /// <summary>
        /// 从配置初始化，预分配 Cn² 环形缓冲区
        /// </summary>
        public LidarInverter(LidarAlgorithmConfig config)
        {
            _config = config;
            _frameCount = 0;
            _ch1History = new double[config.Cn2WindowFrames][];
            _ch2History = new double[config.Cn2WindowFrames][];
            _ringIndex = 0;
        }

        /// <summary>
        /// 单帧反演入口
        /// </summary>
        /// <param name="voltageBlock">上游 ADDraw 产出的电压数据块</param>
        /// <param name="chSel">通道选择：1=CH1, 2=CH2, 3=双通道</param>
        /// <returns>(vis: 整帧能见度 km, cn2Profile: 逐距离门 Cn² 剖面 m⁻²/³)</returns>
        public (double vis, double[] cn2Profile) Invert(Voltage_block voltageBlock, byte chSel)
        {
            _frameCount++;
            int count = voltageBlock.SampleCount;
            double[] cn2Profile = new double[count];

            if (count == 0)
                return (-1.0, cn2Profile);

            double deltaR = C / (2.0 * _config.SampleRateHz); // 距离门宽度
            double blindZone = _config.BlindZoneDistance_m;
            bool isDualChannel = chSel == 3;

            var v1 = voltageBlock.Voltage1;
            var v2 = voltageBlock.Voltage2;

            // ============================================================
            // 第一步：暗电流估计与扣除
            // ============================================================
            double darkCurrentCh1 = EstimateDarkCurrent(v1, count);
            double darkCurrentCh2 = EstimateDarkCurrent(v2, count);

            double[] corrected1 = null;
            double[] corrected2 = null;

            if (v1 != null)
            {
                corrected1 = new double[count];
                for (int i = 0; i < count; i++)
                    corrected1[i] = v1[i] - darkCurrentCh1;
            }

            if (v2 != null)
            {
                corrected2 = new double[count];
                for (int i = 0; i < count; i++)
                    corrected2[i] = v2[i] - darkCurrentCh2;
            }

            // ============================================================
            // 第二步：近场盲区掩蔽 + 距离平方校正
            // 第三步：双通道增益均衡
            // ============================================================
            // 浮点精度处理：暗电流扣除后 < 1e-12 的残余值钳位为 0，
            // 避免 IEEE 754 累积误差经 r² 放大后进入 ln 域扰乱线性回归
            double gainCoeff = _config.GainEqualizationCoefficient;

            for (int i = 0; i < count; i++)
            {
                double r = (i + 1) * deltaR;

                // 近场盲区：r < blindZone 时电压置零
                if (r < blindZone)
                {
                    if (corrected1 != null) corrected1[i] = 0;
                    if (corrected2 != null) corrected2[i] = 0;
                    continue;
                }

                double rSq = r * r;

                if (corrected1 != null)
                {
                    if (corrected1[i] > 1e-12)
                        corrected1[i] *= rSq; // 距离平方校正
                    else
                        corrected1[i] = 0;
                }

                if (corrected2 != null)
                {
                    if (corrected2[i] > 1e-12)
                    {
                        corrected2[i] *= rSq; // 距离平方校正
                        if (isDualChannel)
                            corrected2[i] *= gainCoeff; // 通道增益均衡
                    }
                    else
                    {
                        corrected2[i] = 0;
                    }
                }
            }

            // ============================================================
            // 第四步：Fernald (Klett) 后向积分法计算整帧能见度 Vis
            // ============================================================
            double[] visVoltage = chSel switch
            {
                1 => corrected1 ?? Array.Empty<double>(),
                2 => corrected2 ?? Array.Empty<double>(),
                3 => AverageArrays(corrected1, corrected2, count),
                _ => corrected1 ?? Array.Empty<double>()
            };

            double vis = visVoltage.Length > 0
                ? ComputeFernaldVisPreprocessed(visVoltage, count, deltaR, blindZone)
                : -1.0;

            // ============================================================
            // 第五步：闪烁方差法计算 Cn² 剖面（仅双通道模式）
            // ============================================================
            if (!isDualChannel || corrected1 == null || corrected2 == null)
            {
                // 单通道模式：Cn² 全填 -1.0（哨兵值，物理 Cn² 恒正，-1 天然区分）
                Array.Fill(cn2Profile, -1.0);
            }
            else
            {
                // 将当前帧校准后电压写入环形缓冲区
                _ch1History[_ringIndex] = corrected1;
                _ch2History[_ringIndex] = corrected2;
                _ringIndex = (_ringIndex + 1) % _config.Cn2WindowFrames;

                if (_frameCount < _config.Cn2WindowFrames)
                {
                    // 窗口未满：Cn² 全填 -1.0，下游可区分"未就绪"与"真实值为 0"
                    Array.Fill(cn2Profile, -1.0);
                }
                else
                {
                    // 窗口已满：逐距离门计算 σI² → Cn² = K × σI² × D^(7/3) × L^(-11/6)
                    ComputeCn2Profile(cn2Profile, count);
                }
            }

            return (vis, cn2Profile);
        }

        /// <summary>
        /// 从帧尾部取 DarkCurrentSampleCount 个点求均值作为暗电流估计
        /// </summary>
        private double EstimateDarkCurrent(double[] voltage, int count)
        {
            if (voltage == null || voltage.Length == 0)
                return 0;

            int darkCount = _config.DarkCurrentSampleCount;
            if (darkCount <= 0)
                return 0; // 禁用暗电流扣除
            if (darkCount > count)
                darkCount = count; // 窗口超出帧范围：使用全帧均值

            int start = count - darkCount;
            double sum = 0;
            for (int i = start; i < count; i++)
                sum += voltage[i];
            return sum / darkCount;
        }

        /// <summary>
        /// Fernald (Klett) 能见度反演——远半段线性回归求消光系数
        /// </summary>
        /// <param name="rangeCorrected">已经过距离平方校正的信号 S[i] = V[i] × r[i]²</param>
        private double ComputeFernaldVisPreprocessed(double[] rangeCorrected, int count, double deltaR, double blindZone)
        {
            // 统计盲区外有效点
            int validCount = 0;
            for (int i = 0; i < count; i++)
            {
                double r = (i + 1) * deltaR;
                if (r < blindZone) continue;
                if (rangeCorrected[i] <= 0) continue;
                validCount++;
            }

            if (validCount < 10)
                return -1.0;

            // 收集有效点的距离和 log-校正信号
            Span<double> rVals = stackalloc double[validCount];
            Span<double> lnSVals = stackalloc double[validCount];
            int idx = 0;
            for (int i = 0; i < count; i++)
            {
                double r = (i + 1) * deltaR;
                if (r < blindZone) continue;
                if (rangeCorrected[i] <= 0) continue;

                rVals[idx] = r;
                lnSVals[idx] = Math.Log(rangeCorrected[i]);
                idx++;
            }

            // 对远半段做线性回归（避免近场重叠函数和边界噪声干扰）
            int farStart = validCount / 2;
            int farCount = validCount - farStart;
            if (farCount < 5) farCount = validCount;

            double sumR = 0, sumLnS = 0, sumRR = 0, sumRLnS = 0;
            for (int i = farStart; i < validCount; i++)
            {
                double r = rVals[i];
                double lnS = lnSVals[i];
                sumR += r;
                sumLnS += lnS;
                sumRR += r * r;
                sumRLnS += r * lnS;
            }
            int n = farCount;
            // 斜率法：ln(S) vs r 线性回归，α = -slope / 2
            double slope = (n * sumRLnS - sumR * sumLnS) / (n * sumRR - sumR * sumR);
            double alpha = -slope / 2.0;

            if (alpha <= 0)
                return -1.0;

            // Koschmieder 公式：Vis = 3.912 / α（5% 对比度阈值）
            return 3.912 / alpha;
        }

        /// <summary>
        /// 闪烁方差法计算 Cn² 剖面（球面波形式）
        /// σI² = Var(ln(V1/V2)) 对窗口内 N 帧，Cn² = K × σI² × D^(7/3) × L^(-11/6)
        /// </summary>
        private void ComputeCn2Profile(double[] cn2Profile, int count)
        {
            int windowSize = _config.Cn2WindowFrames;
            double dPow = Math.Pow(_config.ReceiverApertureD_m, 7.0 / 3.0);
            double lPow = Math.Pow(_config.PathLengthL_m, -11.0 / 6.0);
            double coeff = _config.KConstant * dPow * lPow; // 预计算常数因子

            for (int i = 0; i < count; i++)
            {
                // 逐距离门收集窗口内全部帧的 ln(V1/V2)
                double sum = 0, sumSq = 0;
                int validFrames = 0;
                for (int f = 0; f < windowSize; f++)
                {
                    double c1 = _ch1History[f][i];
                    double c2 = _ch2History[f][i];
                    if (c1 <= 0 || c2 <= 0) continue; // 跳过无效点（盲区或弱信号）
                    double logRatio = Math.Log(c1 / c2);
                    sum += logRatio;
                    sumSq += logRatio * logRatio;
                    validFrames++;
                }

                if (validFrames < 2)
                {
                    cn2Profile[i] = -1.0; // 有效帧不足，输出哨兵值
                }
                else
                {
                    double mean = sum / validFrames;
                    double variance = sumSq / validFrames - mean * mean; // 归一化闪烁方差 σI²
                    if (variance < 0) variance = 0;
                    cn2Profile[i] = coeff * variance;
                }
            }
        }

        /// <summary>
        /// 双通道电压逐点求均值（用于 Vis 计算的输入）
        /// </summary>
        private double[] AverageArrays(double[] a, double[] b, int count)
        {
            double[] result = new double[count];
            for (int i = 0; i < count; i++)
            {
                double va = a != null && i < a.Length ? a[i] : 0;
                double vb = b != null && i < b.Length ? b[i] : 0;
                result[i] = (va + vb) / 2.0;
            }
            return result;
        }
    }
}
