using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WebAPI.Models
{
    /// <summary>
    /// 通用命令发送参数（用于 MQTT RPC 调用，对应 POST /api/collector/command）
    /// </summary>
    public class CommandSendParams
    {
        /// <summary>
        /// 命令字符串内容（如 OPEN_DEVICE、START_AD 等）
        /// </summary>
        public string Command { get; set; } = string.Empty;
    }

    /// <summary>
    /// 日志查询请求参数（对应 GET /api/logs 的 query string）
    /// </summary>
    public class LogQueryParams
    {
        /// <summary>
        /// 返回日志条数限制，默认 100，最大 1000
        /// </summary>
        public int Limit { get; set; } = 100;

        /// <summary>
        /// 跳过前 N 条日志，默认 0
        /// </summary>
        public int Offset { get; set; } = 0;

        /// <summary>
        /// 日志级别过滤（如 Information、Error），为 null 表示不过滤
        /// </summary>
        public string? Level { get; set; }

        /// <summary>
        /// 起始时间（ISO 8601 格式），为 null 表示不限
        /// </summary>
        public DateTime? From { get; set; }

        /// <summary>
        /// 结束时间（ISO 8601 格式），为 null 表示不限
        /// </summary>
        public DateTime? To { get; set; }
    }

    /// <summary>
    /// 按日志级别查询请求参数（对应 GET /api/logs/{level}）
    /// </summary>
    public class LogByLevelParams
    {
        /// <summary>
        /// 日志级别名称（如 Information、Error、Warning）
        /// </summary>
        public string Level { get; set; } = string.Empty;

        /// <summary>
        /// 返回日志条数限制，默认 100
        /// </summary>
        public int Limit { get; set; } = 100;
    }

    /// <summary>
    /// 日志查询响应结果
    /// </summary>
    public class LogQueryResult
    {
        /// <summary>
        /// 过滤后日志总数
        /// </summary>
        public int Total { get; set; }

        /// <summary>
        /// 请求的限制数
        /// </summary>
        public int Limit { get; set; }

        /// <summary>
        /// 跳过的偏移量
        /// </summary>
        public int Offset { get; set; }

        /// <summary>
        /// 实际返回的日志条目数
        /// </summary>
        public int Count { get; set; }

        /// <summary>
        /// 日志条目列表
        /// </summary>
        public List<LogEntryDto> Logs { get; set; } = new List<LogEntryDto>();
    }

    /// <summary>
    /// 按级别查询日志响应结果
    /// </summary>
    public class LogByLevelResult
    {
        /// <summary>
        /// 查询的日志级别
        /// </summary>
        public string Level { get; set; } = string.Empty;

        /// <summary>
        /// 返回的日志条目数
        /// </summary>
        public int Count { get; set; }

        /// <summary>
        /// 日志条目列表
        /// </summary>
        public List<LogEntryDto> Logs { get; set; } = new List<LogEntryDto>();
    }

    /// <summary>
    /// 日志级别统计响应结果
    /// </summary>
    public class LogStatsResult
    {
        /// <summary>
        /// 日志总数
        /// </summary>
        public int TotalLogs { get; set; }

        /// <summary>
        /// 各日志级别的统计信息
        /// </summary>
        public List<LogLevelStatItem> Statistics { get; set; } = new List<LogLevelStatItem>();
    }

    /// <summary>
    /// 单个日志级别的统计条目
    /// </summary>
    public class LogLevelStatItem
    {
        /// <summary>
        /// 日志级别名称
        /// </summary>
        public string Level { get; set; } = string.Empty;

        /// <summary>
        /// 该级别日志数量
        /// </summary>
        public int Count { get; set; }

        /// <summary>
        /// 该级别最新日志时间戳
        /// </summary>
        public DateTimeOffset Latest { get; set; }
    }

    /// <summary>
    /// 日志条目数据传输对象
    /// 用于 MQTT RPC 日志查询响应，与 LogController 中的 LogEntryDto 定义保持一致
    /// </summary>
    public class LogEntryDto
    {
        /// <summary>
        /// 日志时间戳
        /// </summary>
        public DateTimeOffset Timestamp { get; set; }

        /// <summary>
        /// 日志级别（字符串表示）
        /// </summary>
        public string Level { get; set; } = string.Empty;

        /// <summary>
        /// 日志消息
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 异常信息（如果有）
        /// </summary>
        public string? Exception { get; set; }

        /// <summary>
        /// 日志属性字典
        /// </summary>
        public Dictionary<string, string> Properties { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// MQTT 状态变更事件数据（用于 daq/{machineId}/events/state_changed 主题发布）
    /// </summary>
    public class MqttStateChangedEvent
    {
        /// <summary>
        /// 事件类型（如 collector_connected、laser_disconnected 等）
        /// </summary>
        public string EventType { get; set; } = string.Empty;

        /// <summary>
        /// 事件来源（collector / laser / system）
        /// </summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>
        /// 事件原因描述
        /// </summary>
        public string Reason { get; set; } = string.Empty;

        /// <summary>
        /// 事件消息内容
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 事件发生时的系统状态快照
        /// 应用 JsonIgnore 条件，为 null 时不序列化
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public SystemStateDto? State { get; set; }

        /// <summary>
        /// 事件时间戳
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}
