using System;

namespace WebAPI.Models
{
    public struct System_State_struct
    {
        /// <summary>
        /// 服务器状态
        /// </summary>
        public ServerState_struct Server;

        /// <summary>
        /// 采集卡状态
        /// </summary>
        public Collector_State_struct Collector;

        /// <summary>
        /// 激光器状态
        /// </summary>
        public Laser_State_struct Laser;
    }

    public struct Collector_State_struct
    {
        /// <summary>
        /// 采集子进程是否已连接
        /// </summary>
        public bool ProcessConnected;

        /// <summary>
        /// 采集设备是否已打开
        /// </summary>
        public bool DeviceOpened;

        /// <summary>
        /// 是否正在采集
        /// </summary>
        public bool Acquiring;

        /// <summary>
        /// 设备句柄
        /// </summary>
        public long Handle;
    }

    public struct Laser_State_struct
    {
        /// <summary>
        /// 激光串口是否已连接
        /// </summary>
        public bool SerialConnected;

        /// <summary>
        /// 激光是否发射中
        /// </summary>
        public bool EmissionOn;

        /// <summary>
        /// 串口名称（模拟值）
        /// </summary>
        public string PortName;
    }

    public struct ServerState_struct
    {
        /// <summary>
        /// API是否存活
        /// </summary>
        public bool IsApiAlive;
    }
}