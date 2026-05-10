using System;
using System.Buffers;
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
        // 存储的是从 ArrayPool 租借的数组引用，覆盖旧帧时 Return 回池
        private readonly double[][] _ch1History;
        private readonly double[][] _ch2History;
        private int _ringIndex;

        private const double C = 299_792_458.0;

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
        /// <returns>(vis: 整帧能见度 km, cn2Profile: 逐距离门 Cn² 剖面 m⁻²/³)
        /// cn2Profile 从 ArrayPool 租借，调用方用完后须 ArrayPool&lt;double&gt;.Shared.Return(cn2Profile)</returns>
        public (double vis, double[] cn2Profile) Invert(Voltage_block voltageBlock, byte chSel)
        {
            _frameCount++;
            int count = voltageBlock.SampleCount;
            var pool = ArrayPool<double>.Shared;

            // cn2Profile 返回给调用方，从池租借
            double[] cn2Profile = pool.Rent(count);

            if (count == 0)
            {
                pool.Return(cn2Profile);
                return (-1.0, Array.Empty<double>());
            }

            double deltaR = C / (2.0 * _config.SampleRateHz);
            double blindZone = _config.BlindZoneDistance_m;
            bool isDualChannel = chSel == 3;

            var v1 = voltageBlock.Voltage1;
            var v2 = voltageBlock.Voltage2;

            // 第一步：暗电流估计
            double darkCurrentCh1 = EstimateDarkCurrent(v1, count);
            double darkCurrentCh2 = EstimateDarkCurrent(v2, count);

            // 第二步 + 第三步（合并循环）：从池租借临时数组
            double[] corrected1 = pool.Rent(count);
            double[] corrected2 = pool.Rent(count);

            double gainCoeff = _config.GainEqualizationCoefficient;

            for (int i = 0; i < count; i++)
            {
                double r = (i + 1) * deltaR;

                if (r < blindZone)
                {
                    corrected1[i] = 0;
                    corrected2[i] = 0;
                    continue;
                }

                double rSq = r * r;

                if (v1 != null)
                {
                    double val = v1[i] - darkCurrentCh1;
                    corrected1[i] = val > 1e-12 ? val * rSq : 0;
                }
                else
                {
                    corrected1[i] = 0;
                }

                if (v2 != null)
                {
                    double val = v2[i] - darkCurrentCh2;
                    if (val > 1e-12)
                    {
                        val *= rSq;
                        if (isDualChannel)
                            val *= gainCoeff;
                        corrected2[i] = val;
                    }
                    else
                    {
                        corrected2[i] = 0;
                    }
                }
                else
                {
                    corrected2[i] = 0;
                }
            }

            // 第四步：Fernald 能见度
            double vis;

            if (chSel == 1 && v1 != null)
            {
                vis = ComputeFernaldVis(corrected1, count, deltaR, blindZone, pool);
            }
            else if (chSel == 2 && v2 != null)
            {
                vis = ComputeFernaldVis(corrected2, count, deltaR, blindZone, pool);
            }
            else if (chSel == 3 && v1 != null && v2 != null)
            {
                double[] avg = pool.Rent(count);
                for (int i = 0; i < count; i++)
                    avg[i] = (corrected1[i] + corrected2[i]) / 2.0;
                vis = ComputeFernaldVis(avg, count, deltaR, blindZone, pool);
                pool.Return(avg);
            }
            else
            {
                vis = -1.0;
            }

            // 第五步：Cn² 剖面（仅双通道模式）
            if (!isDualChannel || v1 == null || v2 == null)
            {
                Array.Fill(cn2Profile, -1.0, 0, count);
                // 归还临时数组
                pool.Return(corrected1);
                pool.Return(corrected2);
            }
            else
            {
                // 环形缓冲区：归还即将被覆盖的旧帧，存入当前帧
                if (_ch1History[_ringIndex] != null)
                    pool.Return(_ch1History[_ringIndex]);
                if (_ch2History[_ringIndex] != null)
                    pool.Return(_ch2History[_ringIndex]);

                _ch1History[_ringIndex] = corrected1;
                _ch2History[_ringIndex] = corrected2;
                _ringIndex = (_ringIndex + 1) % _config.Cn2WindowFrames;

                if (_frameCount < _config.Cn2WindowFrames)
                {
                    Array.Fill(cn2Profile, -1.0, 0, count);
                }
                else
                {
                    ComputeCn2Profile(cn2Profile, count);
                }
            }

            return (vis, cn2Profile);
        }

        private double EstimateDarkCurrent(double[] voltage, int count)
        {
            if (voltage == null || voltage.Length == 0)
                return 0;

            int darkCount = _config.DarkCurrentSampleCount;
            if (darkCount <= 0)
                return 0;
            if (darkCount > count)
                darkCount = count;

            int start = count - darkCount;
            double sum = 0;
            for (int i = start; i < count; i++)
                sum += voltage[i];
            return sum / darkCount;
        }

        /// <summary>
        /// Fernald (Klett) 能见度反演。rVals/lnSVals 从 ArrayPool 租借，方法内归还。
        /// </summary>
        private static double ComputeFernaldVis(double[] rangeCorrected, int count, double deltaR, double blindZone, ArrayPool<double> pool)
        {
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

            double[] rVals = pool.Rent(validCount);
            double[] lnSVals = pool.Rent(validCount);
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
            double slope = (n * sumRLnS - sumR * sumLnS) / (n * sumRR - sumR * sumR);
            double alpha = -slope / 2.0;

            pool.Return(rVals);
            pool.Return(lnSVals);

            if (alpha <= 0)
                return -1.0;

            return 3.912 / alpha;
        }

        /// <summary>
        /// 闪烁方差法计算 Cn² 剖面（球面波形式）
        /// </summary>
        private void ComputeCn2Profile(double[] cn2Profile, int count)
        {
            int windowSize = _config.Cn2WindowFrames;
            double dPow = Math.Pow(_config.ReceiverApertureD_m, 7.0 / 3.0);
            double lPow = Math.Pow(_config.PathLengthL_m, -11.0 / 6.0);
            double coeff = _config.KConstant * dPow * lPow;

            for (int i = 0; i < count; i++)
            {
                double sum = 0, sumSq = 0;
                int validFrames = 0;
                for (int f = 0; f < windowSize; f++)
                {
                    double c1 = _ch1History[f][i];
                    double c2 = _ch2History[f][i];
                    if (c1 <= 0 || c2 <= 0) continue;
                    double logRatio = Math.Log(c1 / c2);
                    sum += logRatio;
                    sumSq += logRatio * logRatio;
                    validFrames++;
                }

                if (validFrames < 2)
                {
                    cn2Profile[i] = -1.0;
                }
                else
                {
                    double mean = sum / validFrames;
                    double variance = sumSq / validFrames - mean * mean;
                    if (variance < 0) variance = 0;
                    cn2Profile[i] = coeff * variance;
                }
            }
        }
    }
}
