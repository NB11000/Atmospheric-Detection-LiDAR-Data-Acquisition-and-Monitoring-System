using System;

namespace WebAPI.Models
{
    /// <summary>
    /// 状态变更推送事件
    /// </summary>
    public class StateChangedEvent
    {
        /// <summary>
        /// 事件类型：collector_disconnected, laser_disconnected 等
        /// </summary>
        public string EventType { get; set; } = string.Empty;

        /// <summary>
        /// 事件来源：collector, laser, server
        /// </summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>
        /// 变更原因
        /// </summary>
        public string Reason { get; set; } = string.Empty;

        /// <summary>
        /// 给UI展示的消息
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 变更后的最新系统状态
        /// </summary>
        public SystemStateDto State { get; set; } = new();

        /// <summary>
        /// 事件时间戳
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}
