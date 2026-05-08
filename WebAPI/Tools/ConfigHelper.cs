using Grpc.Net.Client.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;
using WebAPI;
using WebAPI.Controllers;
using System.Runtime.CompilerServices;
using WebAPI.Models;

[assembly: InternalsVisibleTo("WebAPI.Tests")]

namespace WebAPI.Tools
{
    /// <summary>
    /// 配置文件读写辅助类
    /// </summary>
    public class ConfigHelper
    {

        /// <summary>
        /// 配置文件路径（可执行文件工作目录，方便用户手动修改配置）
        /// </summary>
        public static readonly string ConfigFilePath = Path.Combine(
            AppContext.BaseDirectory,  // 替换为可执行文件工作目录
            "Config", "DeviceConfig.json");      // 配置文件名（直接放在可执行文件同目录）

        /// <summary>
        /// 构建.NET配置提供程序
        /// SetBasePath:定义了「配置文件搜索的根目录」
        /// AddJsonFile:添加json文件
        /// </summary>
        public static IConfigurationRoot Config{ get; set;} = new ConfigurationBuilder()
                .SetBasePath(Path.GetDirectoryName(ConfigFilePath)!)
                .AddJsonFile("DeviceConfig.json", optional: false, reloadOnChange: true)
                .Build();

        /// <summary>
        /// 
        /// </summary>
        private readonly IOptionsMonitor<CaptureCardConfig> captureCardConfig;

        private readonly IOptionsMonitor<RadarConfig> radarConfig;

        private readonly IOptionsMonitor<LidarAlgorithmConfig> lidarConfig;

        private readonly IOptionsMonitor<PersistenceSettings> persistenceSettings;

        private readonly ILogger<ConfigHelper> _logger;


        /// <summary>
        /// 通过构造方法依赖注入
        /// </summary>
        public ConfigHelper(
            IOptionsMonitor<CaptureCardConfig> captureCardConfig,
            IOptionsMonitor<RadarConfig> radarConfig,
            IOptionsMonitor<LidarAlgorithmConfig> lidarConfig,
            IOptionsMonitor<PersistenceSettings> persistenceSettings,
            ILogger<ConfigHelper> logger)
        {
            this.captureCardConfig = captureCardConfig;
            this.radarConfig = radarConfig;
            this.lidarConfig = lidarConfig;
            this.persistenceSettings = persistenceSettings;
            _logger = logger;
        }

        /// <summary>
        /// 从JSON文件读取采集卡设备配置（通过.NET配置提供程序）
        /// </summary>
        /// <returns>DeviceConfig实例</returns>
        public void ReadDeviceConfig()
        {
            ConfigHelper.Config.Reload(); // 重新加载配置文件，确保全局配置实体更新
            // 读取"Capture card"节点下的所有配置项
            Program.CurrentConfig.DeviceId = captureCardConfig.CurrentValue.DeviceId;
            Program.CurrentConfig.SyncChannelIndex = captureCardConfig.CurrentValue.SyncChannelIndex;
            Program.CurrentConfig.SampleRate = captureCardConfig.CurrentValue.SampleRate;
            Program.CurrentConfig.ClockSourceIndex = captureCardConfig.CurrentValue.ClockSourceIndex;
            Program.CurrentConfig.HalfFullThreshold = captureCardConfig.CurrentValue.HalfFullThreshold;
            Program.CurrentConfig.TriggerSourceIndex = captureCardConfig.CurrentValue.TriggerSourceIndex;
            Program.CurrentConfig.RangeIndex = captureCardConfig.CurrentValue.RangeIndex;
            // 重新计算采样周期（确保值最新）
            Program.CurrentConfig.RecalculateSamplePeriod();
        }

        /// <summary>
        /// 从JSON文件读取激光雷达配置（通过.NET配置提供程序）
        /// </summary>
        public void ReadRadarDeviceConfig()
        {
            ConfigHelper.Config.Reload(); // 重新加载配置文件，确保全局配置实体更新
            // 读取"Radar"节点下的所有配置项
            Program.RadarConfig.LaserPower = radarConfig.CurrentValue.LaserPower;
            Program.RadarConfig.LaserModulationFrequency = radarConfig.CurrentValue.LaserModulationFrequency;
            Program.RadarConfig.SerialPort = radarConfig.CurrentValue.SerialPort;
            Program.RadarConfig.BaudRate = radarConfig.CurrentValue.BaudRate;
        }

        ///// <summary>
        ///// 将采集卡配置写入 JSON 文件
        ///// </summary>
        ///// <param name="captureCardConfig">采集卡配置实例</param>
        //public void WriteCaptureCardConfig(CaptureCardConfig captureCardConfig)
        //{
        //    // 1. 读取现有 JSON 配置（避免覆盖雷达等其他节点）
        //    var rootConfig = new RootConfig();
        //    if (File.Exists(ConfigFilePath))
        //    {
        //        // 读取现有文件内容
        //        var existingJson = File.ReadAllText(ConfigFilePath);
        //        // 反序列化（源生成器）
        //        rootConfig = JsonSerializer.Deserialize(existingJson, ConfigJsonContext.Default.RootConfig) ?? new RootConfig();

        //    }

        //    // 2. 更新采集卡配置
        //    rootConfig.CaptureCard = captureCardConfig;

        //    //3.配置 JSON 序列化选项（美化格式、兼容中文、忽略空值）
        //    var jsonOptions = new JsonSerializerOptions
        //    {
        //        WriteIndented = true, // 格式化输出（易读）
        //        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 不转义中文
        //        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, // 忽略空值属性
        //        Converters = { new JsonDecimalConverter() } // 处理 decimal 类型序列化
        //    };
        //    var jsonContext = new ConfigJsonContext(jsonOptions);

        //    // 4. 写入文件（序列化）（源生成器）
        //    var jsonString = JsonSerializer.Serialize(rootConfig, jsonContext.RootConfig);
        //    File.WriteAllText(ConfigFilePath, jsonString, Encoding.UTF8);
        //    // 5. 重新计算采样周期（确保配置一致性）
        //    captureCardConfig.RecalculateSamplePeriod();
        //}

        /// <summary>
        /// 将采集卡配置写入 JSON 文件
        /// </summary>
        /// <param name="captureCardConfig">采集卡配置实例</param>
        public void WriteCaptureCardConfig(CaptureCardConfig captureCardConfig)
        {
            try
            {
                // 5. 重新计算采样周期（确保配置一致性）
                captureCardConfig.RecalculateSamplePeriod();
                //1.读取现有 JSON 配置（避免覆盖雷达等其他节点）
                var rootConfig = new RootConfig();
                if (File.Exists(ConfigFilePath))
                {
                    // 读取现有文件内容
                    var existingJson = File.ReadAllText(ConfigFilePath);
                    // 反序列化（源生成器）
                    rootConfig = JsonSerializer.Deserialize(existingJson, ConfigJsonContext.Default.RootConfig) ?? new RootConfig();

                }

                // 2. 更新采集卡配置
                rootConfig.CaptureCard = captureCardConfig;

                //3.配置 JSON 序列化选项（美化格式、兼容中文、忽略空值）
                var jsonOptions = new JsonSerializerOptions
                {
                    WriteIndented = true, // 格式化输出（易读）
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 不转义中文
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, // 忽略空值属性
                    Converters = { new JsonDecimalConverter() } // 处理 decimal 类型序列化
                };
                var jsonContext = new ConfigJsonContext(jsonOptions);

                // 4. 写入文件（序列化）（源生成器）
                var jsonString = JsonSerializer.Serialize(rootConfig, jsonContext.RootConfig);

                // 5.关键：改用 FileStream 强制刷新磁盘，取消缓存延迟，使用 FileMode.OpenOrCreate + FileAccess.Write
                // 不删除文件，只打开并修改内容！IOptionsMonitor 不会丢失监听！
                using (var stream = new FileStream(
                    ConfigFilePath,
                    FileMode.Open,  // 这个是关键！不是 Create！
                    FileAccess.Write,
                    FileShare.ReadWrite,
                    4096,
                    FileOptions.WriteThrough))  // 直写磁盘，无缓存延迟
                {
                    // 清空文件原有内容（关键！）
                    stream.SetLength(0);
                    using (var writer = new StreamWriter(stream, Encoding.UTF8))
                    {
                        writer.Write(jsonString);
                        writer.Flush();
                        stream.Flush(true);  // 强制写入物理磁盘，禁用操作系统缓存
                    }
                }


                _logger.LogInformation($"文件已写入：{ConfigFilePath} | 最后修改时间：{File.GetLastWriteTime(ConfigFilePath)}");
            }
            catch (UnauthorizedAccessException ex)
            {
                // 权限问题（最常见）
                throw new InvalidOperationException($"无写入权限！请检查路径：{ConfigFilePath}。错误：{ex.Message}", ex);
            }
            catch (IOException ex)
            {
                // 文件被占用/路径错误
                throw new InvalidOperationException($"文件写入失败！路径：{ConfigFilePath}。错误：{ex.Message}", ex);
            }
            catch (Exception ex)
            {
                // 其他异常
                throw new InvalidOperationException($"配置写入失败：{ex.Message}", ex);
            }

        }

        /// <summary>
        /// 将雷达配置写入 JSON 文件
        /// </summary>
        /// <param name="radarConfig">雷达配置实例</param>
        public void WriteRadarConfig(RadarConfig radarConfig)
        {
            try
            {
                //1.读取现有 JSON 配置（避免覆盖采集卡等其他节点）
                var rootConfig = new RootConfig();
                if (File.Exists(ConfigFilePath))
                {
                    // 读取现有文件内容
                    var existingJson = File.ReadAllText(ConfigFilePath);
                    // 反序列化（源生成器）
                    rootConfig = JsonSerializer.Deserialize(existingJson, ConfigJsonContext.Default.RootConfig) ?? new RootConfig();

                }

                // 2. 更新雷达配置
                rootConfig.Radar = radarConfig;

                //3.配置 JSON 序列化选项（美化格式、兼容中文、忽略空值）
                var jsonOptions = new JsonSerializerOptions
                {
                    WriteIndented = true, // 格式化输出（易读）
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 不转义中文
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, // 忽略空值属性
                    Converters = { new JsonDecimalConverter() } // 处理 decimal 类型序列化
                };
                var jsonContext = new ConfigJsonContext(jsonOptions);

                // 4. 写入文件（序列化）（源生成器）
                var jsonString = JsonSerializer.Serialize(rootConfig, jsonContext.RootConfig);

                // 5.关键：改用 FileStream 强制刷新磁盘，取消缓存延迟，使用 FileMode.OpenOrCreate + FileAccess.Write
                // 不删除文件，只打开并修改内容！IOptionsMonitor 不会丢失监听！
                using (var stream = new FileStream(
                    ConfigFilePath,
                    FileMode.Open,  // 这个是关键！不是 Create！
                    FileAccess.Write,
                    FileShare.ReadWrite,
                    4096,
                    FileOptions.WriteThrough))  // 直写磁盘，无缓存延迟
                {
                    // 清空文件原有内容（关键！）
                    stream.SetLength(0);
                    using (var writer = new StreamWriter(stream, Encoding.UTF8))
                    {
                        writer.Write(jsonString);
                        writer.Flush();
                        stream.Flush(true);  // 强制写入物理磁盘，禁用操作系统缓存
                    }
                }

                _logger.LogInformation($"雷达配置已写入：{ConfigFilePath} | 最后修改时间：{File.GetLastWriteTime(ConfigFilePath)}");
            }
            catch (UnauthorizedAccessException ex)
            {
                // 权限问题（最常见）
                throw new InvalidOperationException($"无写入权限！请检查路径：{ConfigFilePath}。错误：{ex.Message}", ex);
            }
            catch (IOException ex)
            {
                // 文件被占用/路径错误
                throw new InvalidOperationException($"文件写入失败！路径：{ConfigFilePath}。错误：{ex.Message}", ex);
            }
            catch (Exception ex)
            {
                // 其他异常
                throw new InvalidOperationException($"雷达配置写入失败：{ex.Message}", ex);
            }

        }

        /// <summary>
        /// 从 DeviceConfig.json 读取反演算法配置（逐字段拷贝到全局静态实体）
        /// </summary>
        public void ReadLidarConfig()
        {
            ConfigHelper.Config.Reload();
            Program.LidarConfig.GainEqualizationCoefficient = lidarConfig.CurrentValue.GainEqualizationCoefficient;
            Program.LidarConfig.KConstant = lidarConfig.CurrentValue.KConstant;
            Program.LidarConfig.ReceiverApertureD_m = lidarConfig.CurrentValue.ReceiverApertureD_m;
            Program.LidarConfig.PathLengthL_m = lidarConfig.CurrentValue.PathLengthL_m;
            Program.LidarConfig.Cn2WindowFrames = lidarConfig.CurrentValue.Cn2WindowFrames;
            Program.LidarConfig.FernaldBoundaryDistance_m = lidarConfig.CurrentValue.FernaldBoundaryDistance_m;
            Program.LidarConfig.LaserWavelength_nm = lidarConfig.CurrentValue.LaserWavelength_nm;
            Program.LidarConfig.AngstromExponent = lidarConfig.CurrentValue.AngstromExponent;
            Program.LidarConfig.DarkCurrentSampleCount = lidarConfig.CurrentValue.DarkCurrentSampleCount;
            Program.LidarConfig.SampleRateHz = lidarConfig.CurrentValue.SampleRateHz;
            Program.LidarConfig.BlindZoneDistance_m = lidarConfig.CurrentValue.BlindZoneDistance_m;
        }

        /// <summary>
        /// 将反演算法配置写入 DeviceConfig.json
        /// </summary>
        public void WriteLidarConfig(LidarAlgorithmConfig config)
        {
            try
            {
                var rootConfig = ReadRootConfig();
                rootConfig.LidarAlgorithm = config;
                WriteRootConfig(rootConfig);
                _logger.LogInformation("反演算法配置已写入：{Path}", ConfigFilePath);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new InvalidOperationException($"无写入权限！请检查路径：{ConfigFilePath}。错误：{ex.Message}", ex);
            }
            catch (IOException ex)
            {
                throw new InvalidOperationException($"文件写入失败！路径：{ConfigFilePath}。错误：{ex.Message}", ex);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"反演配置写入失败：{ex.Message}", ex);
            }
        }

        /// <summary>
        /// 从 DeviceConfig.json 读取持久化配置（逐字段拷贝到全局静态实体）
        /// </summary>
        public void ReadPersistenceConfig()
        {
            ConfigHelper.Config.Reload();
            Program.PersistenceConfig.DataDirectory = persistenceSettings.CurrentValue.DataDirectory;
        }

        private static readonly HashSet<string> WindowsReservedNames = new HashSet<string>(
            new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" },
            StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 将持久化配置写入 DeviceConfig.json
        /// </summary>
        public void WritePersistenceConfig(PersistenceSettings config)
        {
            ValidatePersistenceConfig(config);

            try
            {
                var rootConfig = ReadRootConfig();
                rootConfig.Persistence = config;
                WriteRootConfig(rootConfig);
                _logger.LogInformation("持久化配置已写入：{Path}", ConfigFilePath);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new InvalidOperationException($"无写入权限！请检查路径：{ConfigFilePath}。错误：{ex.Message}", ex);
            }
            catch (IOException ex)
            {
                throw new InvalidOperationException($"文件写入失败！路径：{ConfigFilePath}。错误：{ex.Message}", ex);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"持久化配置写入失败：{ex.Message}", ex);
            }
        }

        internal static void ValidatePersistenceConfig(PersistenceSettings config)
        {
            if (config == null)
                throw new InvalidOperationException("持久化配置不能为空");

            var dir = config.DataDirectory;
            if (string.IsNullOrWhiteSpace(dir))
                throw new InvalidOperationException("数据目录不能为空或仅包含空白字符");

            var invalidChars = Path.GetInvalidFileNameChars()
                .Where(c => c != Path.DirectorySeparatorChar && c != Path.AltDirectorySeparatorChar && c != ':')
                .ToArray();
            if (dir.IndexOfAny(invalidChars) >= 0)
                throw new InvalidOperationException($"数据目录包含非法字符：{dir}");

            var segment = dir.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .LastOrDefault(s => !string.IsNullOrEmpty(s));
            if (segment != null && WindowsReservedNames.Contains(segment))
                throw new InvalidOperationException($"数据目录使用了 Windows 保留名：{segment}");
        }

        private RootConfig ReadRootConfig()
        {
            if (File.Exists(ConfigFilePath))
            {
                var existingJson = File.ReadAllText(ConfigFilePath);
                return JsonSerializer.Deserialize(existingJson, ConfigJsonContext.Default.RootConfig) ?? new RootConfig();
            }
            return new RootConfig();
        }

        private void WriteRootConfig(RootConfig rootConfig)
        {
            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                Converters = { new JsonDecimalConverter() }
            };
            var jsonContext = new ConfigJsonContext(jsonOptions);
            var jsonString = JsonSerializer.Serialize(rootConfig, jsonContext.RootConfig);

            using (var stream = new FileStream(
                ConfigFilePath,
                FileMode.Open,
                FileAccess.Write,
                FileShare.ReadWrite,
                4096,
                FileOptions.WriteThrough))
            {
                stream.SetLength(0);
                using (var writer = new StreamWriter(stream, Encoding.UTF8))
                {
                    writer.Write(jsonString);
                    writer.Flush();
                    stream.Flush(true);
                }
            }
        }

    }



    /// <summary>
    /// 根配置类 - 对应 JSON 顶级结构
    /// </summary>
    public class RootConfig
    {
        /// <summary>
        /// 采集卡配置节点
        /// </summary>
        public CaptureCardConfig CaptureCard { get; set; } = new CaptureCardConfig();

        /// <summary>
        /// 雷达配置节点
        /// </summary>
        public RadarConfig Radar { get; set; } = new RadarConfig();

        /// <summary>
        /// 反演算法配置节点
        /// </summary>
        public LidarAlgorithmConfig LidarAlgorithm { get; set; } = new LidarAlgorithmConfig();

        /// <summary>
        /// 持久化配置节点
        /// </summary>
        public PersistenceSettings Persistence { get; set; } = new PersistenceSettings();
    }

    /// <summary>
    /// Decimal 类型序列化转换器（解决默认科学计数法问题）
    /// </summary>
    public class JsonDecimalConverter : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.GetDecimal();
        }

        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
        {
            // 以普通数字格式输出（避免 1000 变成 1.0E+03）
            writer.WriteRawValue(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }


    /// <summary>
    /// 源生成器 序列化/反序列化
    /// 避免反射
    /// </summary>
    [JsonSerializable(typeof(RootConfig))]
    [JsonSerializable(typeof(CaptureCardConfig))]
    [JsonSerializable(typeof(RadarConfig))]
    [JsonSerializable(typeof(LidarAlgorithmConfig))]
    [JsonSerializable(typeof(PersistenceSettings))]
    [JsonSerializable(typeof(JsonElement))] // 新增：支持JsonElement类型
    public partial class ConfigJsonContext : JsonSerializerContext
    {

    }
}

