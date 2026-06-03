using System;

namespace WebAPI.Models
{
    /// <summary>
    /// 系统统一状态快照(状态机)
    /// </summary>
    public class SystemStateDto
    {
        /// <summary>
        /// 服务器状态
        /// </summary>
        public ServerStateDto Server { get; set; } = new ServerStateDto();

        /// <summary>
        /// 采集卡状态
        /// </summary>
        public CollectorStateDto Collector { get; set; } = new CollectorStateDto();

        /// <summary>
        /// 激光器状态
        /// </summary>
        public LaserStateDto Laser { get; set; } = new LaserStateDto();

        /// <summary>
        /// UI可操作提示状态
        /// </summary>
        public UiHintStateDto UiHints { get; set; } = new UiHintStateDto();

        /// <summary>
        /// 快照生成时间
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// 服务器状态
    /// </summary>
    public class ServerStateDto
    {
        /// <summary>
        /// WebAPI是否存活
        /// </summary>
        public bool IsApiAlive { get; set; }

        /// <summary>
        /// 状态更新时间
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;
        
        /// <summary>
        /// MQTT 连接状态（由 SystemStateService 本地缓存维护，供 API 查询和 UI 显示）
        /// </summary>
        public bool IsMqttConnected { get; internal set; }
    }

    /// <summary>
    /// 采集卡状态
    /// </summary>
    public class CollectorStateDto
    {
        /// <summary>
        /// 采集子进程是否已连接
        /// </summary>
        public bool ProcessConnected { get; set; }

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

    /// <summary>
    /// 激光器状态
    /// </summary>
    public class LaserStateDto
    {
        /// <summary>
        /// 串口是否已连接
        /// </summary>
        public bool SerialConnected { get; set; }

        /// <summary>
        /// 激光是否处于开启状态
        /// </summary>
        public bool EmissionOn { get; set; }

        /// <summary>
        /// 串口名称
        /// </summary>
        public string PortName { get; set; } = string.Empty;

        /// <summary>
        /// 最近一次状态消息
        /// </summary>
        public string LastMessage { get; set; } = string.Empty;

        /// <summary>
        /// 状态更新时间
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// UI可用操作提示
    /// </summary>
    public class UiHintStateDto
    {
        /// <summary>
        /// 是否允许打开采集卡
        /// </summary>
        public bool CanOpenCollector { get; set; }

        /// <summary>
        /// 是否允许关闭采集卡
        /// </summary>
        public bool CanCloseCollector { get; set; }

        /// <summary>
        /// 是否允许开始采集
        /// </summary>
        public bool CanStartAcquisition { get; set; }

        /// <summary>
        /// 是否允许停止采集
        /// </summary>
        public bool CanStopAcquisition { get; set; }

        /// <summary>
        /// 是否允许连接激光串口
        /// </summary>
        public bool CanConnectLaser { get; set; }

        /// <summary>
        /// 是否允许断开激光串口
        /// </summary>
        public bool CanDisconnectLaser { get; set; }

        /// <summary>
        /// 是否允许开启激光
        /// </summary>
        public bool CanTurnLaserOn { get; set; }

        /// <summary>
        /// 是否允许关闭激光
        /// </summary>
        public bool CanTurnLaserOff { get; set; }
    }
}
