using System.Text.Json;
using AvaloniaApplication1.Models;
using ConsoleApp1.Models;
using ConsoleApp1.Service;
using Xunit;

namespace WebAPI.Tests.Lidar;

public class LidarInverterTests
{
    // --- Slice 1: Config tests ---
    [Fact]
    public void LidarAlgorithmConfig_Deserialization_ReadsAllFields()
    {
        const string json = """
        {
            "GainEqualizationCoefficient": 1.05,
            "KConstant": 4.48,
            "ReceiverApertureD_m": 0.2,
            "PathLengthL_m": 1000.0,
            "Cn2WindowFrames": 100,
            "FernaldBoundaryDistance_m": 3000.0,
            "LaserWavelength_nm": 532.0,
            "AngstromExponent": 1.3,
            "DarkCurrentSampleCount": 50
        }
        """;

        var config = JsonSerializer.Deserialize<LidarAlgorithmConfig>(json);

        Assert.NotNull(config);
        Assert.Equal(1.05, config!.GainEqualizationCoefficient);
        Assert.Equal(4.48, config.KConstant);
        Assert.Equal(0.2, config.ReceiverApertureD_m);
        Assert.Equal(1000.0, config.PathLengthL_m);
        Assert.Equal(100, config.Cn2WindowFrames);
        Assert.Equal(3000.0, config.FernaldBoundaryDistance_m);
        Assert.Equal(532.0, config.LaserWavelength_nm);
        Assert.Equal(1.3, config.AngstromExponent);
        Assert.Equal(50, config.DarkCurrentSampleCount);
    }

    [Fact]
    public void LidarAlgorithmConfig_DefaultValues_AreCorrect()
    {
        var config = new LidarAlgorithmConfig();

        Assert.Equal(4.48, config.KConstant);
        Assert.Equal(100, config.Cn2WindowFrames);
        Assert.Equal(1.3, config.AngstromExponent);
        Assert.Equal(1.0, config.GainEqualizationCoefficient);
        Assert.Equal(0.2, config.ReceiverApertureD_m);
        Assert.Equal(1000.0, config.PathLengthL_m);
        Assert.Equal(3000.0, config.FernaldBoundaryDistance_m);
        Assert.Equal(532.0, config.LaserWavelength_nm);
        Assert.Equal(0, config.DarkCurrentSampleCount);
    }

    [Fact]
    public void LidarAlgorithmConfig_FromNestedJson_SectionBindingWorks()
    {
        var json = """
        {
            "LidarAlgorithm": {
                "GainEqualizationCoefficient": 1.15,
                "KConstant": 5.0,
                "ReceiverApertureD_m": 0.3,
                "PathLengthL_m": 2000.0,
                "Cn2WindowFrames": 200,
                "FernaldBoundaryDistance_m": 5000.0,
                "LaserWavelength_nm": 1064.0,
                "AngstromExponent": 1.5,
                "DarkCurrentSampleCount": 100
            }
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var sectionElement = doc.RootElement.GetProperty("LidarAlgorithm");
        var config = JsonSerializer.Deserialize<LidarAlgorithmConfig>(
            sectionElement.GetRawText());

        Assert.NotNull(config);
        Assert.Equal(1.15, config!.GainEqualizationCoefficient);
        Assert.Equal(5.0, config.KConstant);
        Assert.Equal(200, config.Cn2WindowFrames);
    }

    // --- Slice 2: Fernald Vis tests ---

    [Fact]
    public void Fernald_UniformAtmosphere_ReturnsCorrectVis()
    {
        // Homogeneous atmosphere: α = 0.0005 m⁻¹ → Vis = 3.912/α = 7824 m ≈ 7.82 km
        const double alpha = 0.0005;
        const double expectedVis = 3.912 / alpha; // 7824 m
        const int sampleCount = 1000;
        const double sampleRateHz = 30_000_000.0; // 30 MHz
        const double c = 299_792_458.0; // speed of light
        const double deltaR = c / (2.0 * sampleRateHz); // ~5m per bin

        var config = new LidarAlgorithmConfig
        {
            SampleRateHz = sampleRateHz,
            FernaldBoundaryDistance_m = sampleCount * deltaR,
            BlindZoneDistance_m = 5 * deltaR // skip first 5 bins
        };

        // Generate synthetic voltage: V[i] = exp(-2α·r[i]) / r[i]²
        var voltage1 = new double[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            double r = (i + 1) * deltaR;
            voltage1[i] = Math.Exp(-2.0 * alpha * r) / (r * r);
        }

        var voltageBlock = new Voltage_block
        {
            Voltage1 = voltage1,
            Voltage2 = null,
            SampleCount = sampleCount
        };

        var inverter = new LidarInverter(config);
        var (vis, cn2Profile) = inverter.Invert(voltageBlock, chSel: 1);

        Assert.True(vis > 0, $"Vis should be positive, got {vis}");
        double relativeError = Math.Abs(vis - expectedVis) / expectedVis;
        Assert.True(relativeError < 0.05,
            $"Vis {vis:F1}m deviates from expected {expectedVis:F1}m by {relativeError:P1}");

        Assert.Equal(sampleCount, cn2Profile.Length);
        Assert.All(cn2Profile, v => Assert.Equal(-1.0, v));
    }

    [Fact]
    public void Fernald_Ch2Only_UsesChannel2()
    {
        const double alpha = 0.0005;
        const double expectedVis = 3.912 / alpha;
        const int sampleCount = 500;
        const double sampleRateHz = 20_000_000.0;
        const double c = 299_792_458.0;
        const double deltaR = c / (2.0 * sampleRateHz);

        var config = new LidarAlgorithmConfig
        {
            SampleRateHz = sampleRateHz,
            FernaldBoundaryDistance_m = sampleCount * deltaR,
            BlindZoneDistance_m = 3 * deltaR
        };

        // Put signal on CH2 only, CH1 has zero data
        var voltage1 = new double[sampleCount]; // all zeros
        var voltage2 = new double[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            double r = (i + 1) * deltaR;
            voltage2[i] = Math.Exp(-2.0 * alpha * r) / (r * r);
        }

        var voltageBlock = new Voltage_block
        {
            Voltage1 = voltage1,
            Voltage2 = voltage2,
            SampleCount = sampleCount
        };

        var inverter = new LidarInverter(config);
        var (vis, _) = inverter.Invert(voltageBlock, chSel: 2);

        Assert.True(vis > 0, $"CH2 Vis should be positive, got {vis}");
        double relativeError = Math.Abs(vis - expectedVis) / expectedVis;
        Assert.True(relativeError < 0.05,
            $"CH2 Vis {vis:F1}m, expected {expectedVis:F1}m, error {relativeError:P1}");
    }

    [Fact]
    public void Fernald_EmptyFrame_ReturnsSentinelValues()
    {
        var config = new LidarAlgorithmConfig();
        var voltageBlock = new Voltage_block
        {
            Voltage1 = Array.Empty<double>(),
            Voltage2 = null,
            SampleCount = 0
        };

        var inverter = new LidarInverter(config);
        var (vis, cn2Profile) = inverter.Invert(voltageBlock, chSel: 1);

        Assert.Equal(-1.0, vis);
        Assert.Empty(cn2Profile);
    }

    [Fact]
    public void Fernald_DualChannel_ReturnsVisAndSentinelCn2()
    {
        const double alpha = 0.0005;
        const int sampleCount = 500;
        const double sampleRateHz = 20_000_000.0;
        const double c = 299_792_458.0;
        const double deltaR = c / (2.0 * sampleRateHz);

        var config = new LidarAlgorithmConfig
        {
            SampleRateHz = sampleRateHz,
            FernaldBoundaryDistance_m = sampleCount * deltaR,
            BlindZoneDistance_m = 3 * deltaR
        };

        var voltage1 = new double[sampleCount];
        var voltage2 = new double[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            double r = (i + 1) * deltaR;
            voltage1[i] = Math.Exp(-2.0 * alpha * r) / (r * r);
            voltage2[i] = voltage1[i] * 1.1; // slight gain difference
        }

        var voltageBlock = new Voltage_block
        {
            Voltage1 = voltage1,
            Voltage2 = voltage2,
            SampleCount = sampleCount
        };

        var inverter = new LidarInverter(config);
        var (vis, cn2Profile) = inverter.Invert(voltageBlock, chSel: 3);

        Assert.True(vis > 0, $"Dual-channel Vis should be positive, got {vis}");
        Assert.Equal(sampleCount, cn2Profile.Length);
        Assert.All(cn2Profile, v => Assert.Equal(-1.0, v));
    }

    // --- Slice 3: Cn² scintillation inversion tests ---

    [Fact]
    public void Cn2_SingleChannel_ReturnsNegativeOne()
    {
        var config = new LidarAlgorithmConfig();
        var voltage1 = new double[] { 0.1, 0.2, 0.3, 0.4, 0.5 };
        var voltageBlock = new Voltage_block
        {
            Voltage1 = voltage1,
            Voltage2 = null,
            SampleCount = 5
        };

        var inverter = new LidarInverter(config);
        var (vis, cn2Profile) = inverter.Invert(voltageBlock, chSel: 1);

        Assert.All(cn2Profile, v => Assert.Equal(-1.0, v));
    }

    [Fact]
    public void Cn2_Frame100_ReturnsValidValues()
    {
        const int sampleCount = 10;
        const int windowSize = 100;
        var config = new LidarAlgorithmConfig
        {
            Cn2WindowFrames = windowSize,
            SampleRateHz = 30_000_000.0,
            BlindZoneDistance_m = 0,
            FernaldBoundaryDistance_m = 1000
        };

        var inverter = new LidarInverter(config);
        var rng = new Random(42);

        // Feed 100 frames with dual-channel data
        // For range bin 5, add controlled log-ratio variance
        const int testBin = 5;
        var logRatios = new List<double>();
        for (int f = 0; f < windowSize; f++)
        {
            var v1 = new double[sampleCount];
            var v2 = new double[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                double r = (i + 1) * 5.0;
                v1[i] = Math.Exp(-0.001 * r) / (r * r); // base signal
                v2[i] = v1[i]; // same base
            }
            // Add log-normal scintillation to testBin
            double logRatio = (rng.NextDouble() - 0.5) * 0.2; // σ ≈ 0.0577
            logRatios.Add(logRatio);
            v2[testBin] = v1[testBin] * Math.Exp(logRatio);

            var block = new Voltage_block
            {
                Voltage1 = v1,
                Voltage2 = v2,
                SampleCount = sampleCount
            };
            inverter.Invert(block, chSel: 3);
        }

        // Frame 101: verify Cn2 is computed
        var v1_101 = new double[sampleCount];
        var v2_101 = new double[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            double r = (i + 1) * 5.0;
            v1_101[i] = Math.Exp(-0.001 * r) / (r * r);
            v2_101[i] = v1_101[i];
        }
        var block101 = new Voltage_block
        {
            Voltage1 = v1_101,
            Voltage2 = v2_101,
            SampleCount = sampleCount
        };
        var (vis, cn2Profile) = inverter.Invert(block101, chSel: 3);

        // testBin should have valid Cn2
        Assert.True(cn2Profile[testBin] > 0,
            $"Cn2 at bin {testBin} should be > 0, got {cn2Profile[testBin]}");

        // Verify Cn2 roughly matches the formula
        double sigmaSq = logRatios.Average(r => r * r) - Math.Pow(logRatios.Average(), 2);
        double expectedCn2 = config.KConstant * sigmaSq
            * Math.Pow(config.ReceiverApertureD_m, 7.0 / 3.0)
            * Math.Pow(config.PathLengthL_m, -11.0 / 6.0);
        double relativeError = Math.Abs(cn2Profile[testBin] - expectedCn2) / expectedCn2;
        Assert.True(relativeError < 0.30,
            $"Cn2 {cn2Profile[testBin]:E4}, expected ~{expectedCn2:E4}, error {relativeError:P1}");
    }

    [Fact]
    public void Cn2_WindowNotFull_ReturnsNegativeOne()
    {
        const int windowSize = 100;
        var config = new LidarAlgorithmConfig { Cn2WindowFrames = windowSize };
        var inverter = new LidarInverter(config);

        for (int f = 0; f < windowSize - 1; f++)
        {
            var v1 = new double[] { 0.1, 0.2 };
            var v2 = new double[] { 0.11, 0.19 };
            var block = new Voltage_block { Voltage1 = v1, Voltage2 = v2, SampleCount = 2 };
            var (_, cn2) = inverter.Invert(block, chSel: 3);
            Assert.All(cn2, v => Assert.Equal(-1.0, v));
        }
    }

    [Fact]
    public void Cn2_SlidingWindow_UpdatesCorrectly()
    {
        const int windowSize = 50;
        const int sampleCount = 3;
        var config = new LidarAlgorithmConfig
        {
            Cn2WindowFrames = windowSize,
            SampleRateHz = 30_000_000.0,
            BlindZoneDistance_m = 0
        };
        var inverter = new LidarInverter(config);

        // Feed 50 frames with steady data (no variance) → Cn2 ≈ 0
        for (int f = 0; f < windowSize; f++)
        {
            var v1 = new double[sampleCount];
            var v2 = new double[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                v1[i] = 1.0;
                v2[i] = 1.0; // identical → zero variance
            }
            var block = new Voltage_block { Voltage1 = v1, Voltage2 = v2, SampleCount = sampleCount };
            inverter.Invert(block, chSel: 3);
        }

        // Next frame: should have Cn2 ≈ 0 for all bins
        {
            var v1 = new double[sampleCount];
            var v2 = new double[sampleCount];
            Array.Fill(v1, 1.0);
            Array.Fill(v2, 1.0);
            var block = new Voltage_block { Voltage1 = v1, Voltage2 = v2, SampleCount = sampleCount };
            var (_, cn2) = inverter.Invert(block, chSel: 3);
            for (int i = 0; i < sampleCount; i++)
                Assert.True(cn2[i] < 1e-10, $"Cn2[{i}] should be ~0, got {cn2[i]:E4}");
        }

        // Feed a new round with high variance → Cn2 should increase
        for (int f = 0; f < windowSize; f++)
        {
            var v1 = new double[sampleCount];
            var v2 = new double[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                v1[i] = 1.0;
                v2[i] = 1.0 + (f % 3) * 0.5; // modulated → variance
            }
            var block = new Voltage_block { Voltage1 = v1, Voltage2 = v2, SampleCount = sampleCount };
            inverter.Invert(block, chSel: 3);
        }

        // Now Cn2 should be > 0 (non-zero variance)
        {
            var v1 = new double[sampleCount];
            var v2 = new double[sampleCount];
            Array.Fill(v1, 1.0);
            Array.Fill(v2, 1.5);
            var block = new Voltage_block { Voltage1 = v1, Voltage2 = v2, SampleCount = sampleCount };
            var (_, cn2) = inverter.Invert(block, chSel: 3);
            for (int i = 0; i < sampleCount; i++)
                Assert.True(cn2[i] > 0, $"Cn2[{i}] should be > 0 after variance introduced, got {cn2[i]:E4}");
        }
    }

    // --- Slice 4: Preprocessing tests ---

    [Fact]
    public void DarkCurrent_SubtractsCorrectly()
    {
        // Use simple flat signal to verify DC subtraction works
        const double alpha = 0.0005;
        const int sampleCount = 200;
        const double sampleRateHz = 30_000_000.0;
        const double c = 299_792_458.0;
        const double deltaR = c / (2.0 * sampleRateHz);
        const double darkCurrent = 0.05;
        const int darkSamples = 50;

        var config = new LidarAlgorithmConfig
        {
            SampleRateHz = sampleRateHz,
            FernaldBoundaryDistance_m = sampleCount * deltaR,
            BlindZoneDistance_m = 3 * deltaR,
            DarkCurrentSampleCount = darkSamples
        };

        // Build signal: signal region has V_sig + DC, tail has only DC
        var voltage = new double[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            double r = (i + 1) * deltaR;
            double vSig = Math.Exp(-2.0 * alpha * r) / (r * r);
            if (i >= sampleCount - darkSamples)
                voltage[i] = darkCurrent;           // tail: pure DC
            else
                voltage[i] = vSig + darkCurrent;    // signal + DC
        }

        // First: verify a clean signal (no DC) gives correct Vis
        var cleanVoltage = new double[sampleCount - darkSamples];
        for (int i = 0; i < cleanVoltage.Length; i++)
        {
            double r = (i + 1) * deltaR;
            cleanVoltage[i] = Math.Exp(-2.0 * alpha * r) / (r * r);
        }
        var configClean = new LidarAlgorithmConfig
        {
            SampleRateHz = sampleRateHz,
            FernaldBoundaryDistance_m = cleanVoltage.Length * deltaR,
            BlindZoneDistance_m = 3 * deltaR,
            DarkCurrentSampleCount = 0
        };
        var inverterClean = new LidarInverter(configClean);
        var (visClean, _) = inverterClean.Invert(
            new Voltage_block { Voltage1 = cleanVoltage, Voltage2 = null, SampleCount = cleanVoltage.Length }, chSel: 1);
        double expectedVis = 3.912 / alpha;
        Assert.True(visClean > expectedVis * 0.95,
            $"Clean signal Vis={visClean:F1}, expected ~{expectedVis:F1}");

        // Second: verify DC-subtracted signal gives similar Vis to clean signal
        var inverter = new LidarInverter(config);
        var (vis, _) = inverter.Invert(
            new Voltage_block { Voltage1 = voltage, Voltage2 = null, SampleCount = sampleCount }, chSel: 1);

        Assert.True(vis > 0, $"Vis should be positive, got {vis}");
        double relativeError = Math.Abs(vis - expectedVis) / expectedVis;
        Assert.True(relativeError < 0.15,
            $"DC-subtracted Vis {vis:F1}m, expected ~{expectedVis:F1}m, error {relativeError:P1}");
    }

    [Fact]
    public void GainEqualization_BalancesChannels()
    {
        const int sampleCount = 10;
        var config = new LidarAlgorithmConfig
        {
            SampleRateHz = 30_000_000.0,
            BlindZoneDistance_m = 0,
            GainEqualizationCoefficient = 2.0 // CH2 is 2x weaker, compensate with coeff=2
        };

        // CH2 signal is half of CH1 (gain imbalance of 2x)
        var v1 = new double[sampleCount];
        var v2 = new double[sampleCount];
        var rng = new Random(42);
        for (int i = 0; i < sampleCount; i++)
        {
            double r = (i + 1) * 5.0;
            v1[i] = Math.Exp(-0.001 * r) / (r * r);
            v2[i] = v1[i] * 0.5; // 2x weaker
        }

        var voltageBlock = new Voltage_block
        {
            Voltage1 = v1,
            Voltage2 = v2,
            SampleCount = sampleCount
        };

        var inverter = new LidarInverter(config);
        var (vis, _) = inverter.Invert(voltageBlock, chSel: 3);

        Assert.True(vis > 0, $"Vis should be positive after gain equalization, got {vis}");
    }
}
