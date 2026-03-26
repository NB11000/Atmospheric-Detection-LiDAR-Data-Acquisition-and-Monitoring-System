using ConsoleApp1.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ConsoleApp1.Tools
{
    /// <summary>
    /// 配置文件读写辅助类
    /// </summary>
    public class ConfigHelper
    {
        /// <summary>
        /// 配置文件路径（可执行文件工作目录，方便用户手动修改配置）
        /// </summary>
        public static string ConfigFilePath;

        /// <summary>
        /// 构建.NET配置提供程序
        /// SetBasePath:定义了「配置文件搜索的根目录」
        /// AddJsonFile:添加json文件
        /// </summary>
        public static IConfigurationRoot Config;

        /// <summary>
        /// 配置读取器
        /// </summary>
        private readonly IOptionsMonitor<CaptureCardConfig> captureCardConfig;


        /// <summary>
        /// 通过构造方法依赖注入
        /// </summary>
        /// <param name="captureCardConfig"></param>
        public ConfigHelper(IOptionsMonitor<CaptureCardConfig> captureCardConfig)
        {
            this.captureCardConfig = captureCardConfig;
        }

        /// <summary>
        /// 将设备配置写入JSON文件（持久化）
        /// </summary>
        /// <param name="config">要保存的配置实例</param>
        public static void SaveDeviceConfig(CaptureCardConfig deviceconfig)
        {
            if (deviceconfig == null)
                throw new ArgumentNullException(nameof(deviceconfig));

            // 确保配置文件目录存在
            string dir = Path.GetDirectoryName(ConfigFilePath);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // JSON序列化选项（格式化、忽略空值、兼容大小写）
            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,       // 格式化输出（便于阅读）
                PropertyNameCaseInsensitive = true // 大小写不敏感
            };

            // 序列化并写入文件
            string jsonContent = JsonSerializer.Serialize(deviceconfig, jsonOptions);
            File.WriteAllText(ConfigFilePath, jsonContent, Encoding.UTF8);
        }


        /// <summary>
        /// 从JSON文件读取设备配置到实体类（通过.NET配置提供程序）
        /// </summary>
        /// <returns>DeviceConfig实例</returns>
        public void ReadDeviceConfig()
        {
            // 刷新配置
            Config.Reload();
            // 读取"Capture card"节点下的所有配置项
            // 并将配置节绑定到DeviceConfig实体
            Program.deviceconfig.DeviceId = captureCardConfig.CurrentValue.DeviceId;
            Program.deviceconfig.SyncChannelIndex = captureCardConfig.CurrentValue.SyncChannelIndex;
            Program.deviceconfig.SampleRate = captureCardConfig.CurrentValue.SampleRate;
            Program.deviceconfig.ClockSourceIndex = captureCardConfig.CurrentValue.ClockSourceIndex;
            Program.deviceconfig.HalfFullThreshold = captureCardConfig.CurrentValue.HalfFullThreshold;
            Program.deviceconfig.TriggerSourceIndex = captureCardConfig.CurrentValue.TriggerSourceIndex;
            Program.deviceconfig.RangeIndex = captureCardConfig.CurrentValue.RangeIndex;
            // 重新计算采样周期（确保值最新）
            Program.deviceconfig.RecalculateSamplePeriod();
        }

    }
}








