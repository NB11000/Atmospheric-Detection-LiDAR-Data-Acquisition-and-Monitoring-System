using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleApp1.Models
{
    /// <summary>
    /// 采集卡配置参数
    /// </summary>
    public class DeviceConfig
    {

        // 同步通道数：0=通道1，1=通道2，2=通道1和通道2
        public int SyncChannelIndex { get; set; } = 2;

        // 采样频率 (kHz)
        public decimal SampleRate { get; set; } = 1000;

        // 时钟源：0=内时钟，1=外时钟
        public int ClockSourceIndex { get; set; } = 0;

        // 半满阈值 (Word)
        public int HalfFullThreshold { get; set; } = 5;

        // 触发源：0=外触发，1=软触发
        public int TriggerSourceIndex { get; set; } = 1;

        // 量程：0=±5V，1=±10V
        public int RangeIndex { get; set; } = 0;

        // 采样周期（秒）
        // 注意：这个属性不是直接设置的，而是根据采样频率计算得出
        // 单个采样点的时间戳=初始时间戳+采样点索引*采样周期
        public double SamplePeriod;

        public DeviceConfig()
        {
            SamplePeriod= 1 / (double)(SampleRate * 1000);
        }




        // 可选：增加属性转文字的快捷方法，方便主窗口展示
        /// <summary>
        /// 同步通道类型
        /// </summary>
        public string Channel => SyncChannelIndex switch
        {
            0 => "通道1",
            1 => "通道2",
            2 => "通道1和通道2",
            _ => "通道1"
        };

        /// <summary>
        /// 时钟源
        /// </summary>
        public string ClockSource => ClockSourceIndex switch
        {
            0 => "外时钟",
            1 => "内时钟",
            _ => "外时钟"
        };

        /// <summary>
        /// 半满阈值
        /// </summary>
        public string HalfFullThresho => HalfFullThreshold switch
        {
            0 => "2M",
            1 => "1M",
            2 => "512K",
            3 => "256K",
            4 => "128K",
            5 => "64K",
            6 => "32K",
            7 => "16K",
            _ => "2M"
        };

        /// <summary>
        /// 触发源
        /// </summary>
        public string TriggerSource => TriggerSourceIndex switch
        {
            0 => "外触发",
            1 => "软触发",
            _ => "外触发"
        };

        /// <summary>
        /// 量程
        /// </summary>
        public string Range => RangeIndex switch
        {
            0 => "±5V",
            1 => "±10V",
            _ => "±5V"
        };



    }
}
