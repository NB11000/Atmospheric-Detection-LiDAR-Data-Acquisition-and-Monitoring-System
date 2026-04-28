namespace WebAPI.Models
{
    /// <summary>
    /// MQTT 连接与 RPC 配置选项
    /// 通过 appsettings.json 中 "Mqtt" 节点绑定，支持 ASP.NET Core Options 模式
    /// </summary>
    public class MqttSettings
    {
        /// <summary>
        /// MQTT Broker 主机地址
        /// </summary>
        public string BrokerHost { get; set; } = "localhost";

        /// <summary>
        /// MQTT Broker 端口号
        /// </summary>
        public int BrokerPort { get; set; } = 1883;

        /// <summary>
        /// 机器/进程标识，用于构建 MQTT 主题前缀，支持多机部署区分
        /// </summary>
        public string MachineId { get; set; } = "daq-srv-01";

        /// <summary>
        /// MQTT Broker 认证用户名（为空则不认证）
        /// </summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// MQTT Broker 认证密码（为空则不认证）
        /// </summary>
        public string Password { get; set; } = string.Empty;

        /// <summary>
        /// RPC 调用超时秒数
        /// </summary>
        public int RpcTimeoutSeconds { get; set; } = 60;

        /// <summary>
        /// Broker 断线后重连间隔秒数
        /// </summary>
        public int ReconnectDelaySeconds { get; set; } = 5;

        /// <summary>
        /// 波形数据 MQTT 发布间隔毫秒数
        /// </summary>
        public int WaveformPublishIntervalMs { get; set; } = 100;
    }
}
