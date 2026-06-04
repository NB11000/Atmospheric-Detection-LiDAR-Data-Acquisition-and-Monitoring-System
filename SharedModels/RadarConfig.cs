using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

namespace SharedModels
{
    /// <summary>
    /// 雷达配置类
    /// </summary>
    public class RadarConfig
    {
        /// <summary>
        /// 激光功率
        /// </summary>
        public int LaserPower { get; set; } = 0;

        /// <summary>
        /// 激光调制频率
        /// </summary>
        public int LaserModulationFrequency { get; set; } = 0;

        /// <summary>
        /// 串口号
        /// </summary>
        public string SerialPort { get; set; } = "";

        /// <summary>
        /// 波特率
        /// </summary>
        public int BaudRate { get; set; } = 9600;
    }
}
