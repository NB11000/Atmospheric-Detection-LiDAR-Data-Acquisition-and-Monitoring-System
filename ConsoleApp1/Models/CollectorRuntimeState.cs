using System;

namespace ConsoleApp1.Models
{
    /// <summary>
    /// 采集卡运行时状态
    /// </summary>
    public class CollectorRuntimeState
    {
        /// <summary>
        /// 采集卡是否已打开
        /// </summary>
        public bool DeviceOpened { get; set; }

        /// <summary>
        /// 是否正在采集
        /// </summary>
        public bool Acquiring { get; set; }

        /// <summary>
        /// 当前设备句柄
        /// </summary>
        public long Handle { get; set; }

        /// <summary>
        /// 最近一次状态消息
        /// </summary>
        public string LastMessage { get; set; } = string.Empty;

        /// <summary>
        /// 状态更新时间
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}
