using System.Text.Json.Serialization;

namespace SharedModels
{
    /// <summary>
    /// 统一命令响应模型
    /// </summary>
    public class CommandResult
    {
        /// <summary>
        /// 命令是否成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 命令结果码，如 COLLECTOR_OPENED, AD_STARTED
        /// </summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>
        /// 给UI展示的消息
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 命令执行后的最新系统状态
        /// 应用JsonIgnore条件，当字段为null时不序列化，减少网络传输
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public SystemStateDto? State { get; set; }

        /// <summary>
        /// 响应时间戳
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}
