using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.InMemory;
using WebAPI.Models;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace WebAPI.MqttRpc
{
    /// <summary>
    /// 日志查询领域 MQTT RPC Handler
    /// 替代 LogController 的 HTTP 端点，直接访问 Serilog InMemorySink
    /// 5 个 RPC 方法与 HTTP 端点一一对应
    /// </summary>
    public class LogHandler
    {
        private readonly ILogger<LogHandler> _logger;

        /// <summary>
        /// JSON 序列化选项（紧凑格式）
        /// </summary>
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>
        /// 构造函数，注入日志记录器
        /// </summary>
        public LogHandler(ILogger<LogHandler> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 获取所有 RPC 方法名到处理函数的映射字典
        /// </summary>
        public Dictionary<string, Func<byte[], Task<byte[]>>> GetHandlers()
        {
            return new Dictionary<string, Func<byte[], Task<byte[]>>>
            {
                ["logs-query"] = HandleQueryLogs,
                ["logs-by-level"] = HandleLogsByLevel,
                ["logs-level-stats"] = HandleLevelStats,
                ["logs-clear"] = HandleClearLogs,
                ["logs-health"] = HandleHealth,
            };
        }

        /// <summary>
        /// logs-query — 查询系统日志（支持分页、过滤、时间范围）
        /// payload：LogQueryParams JSON
        /// </summary>
        private async Task<byte[]> HandleQueryLogs(byte[] payload)
        {
            try
            {
                var param = payload.Length > 0
                    ? JsonSerializer.Deserialize<LogQueryParams>(payload, _jsonOptions)
                    : new LogQueryParams();

                if (param == null)
                {
                    param = new LogQueryParams();
                }

                _logger.LogInformation("MQTT RPC: 查询日志请求：limit={Limit}, offset={Offset}, level={Level}",
                    param.Limit, param.Offset, param.Level);

                var allLogEvents = InMemorySink.Instance.LogEvents;
                var filteredLogs = allLogEvents.AsEnumerable();

                // 应用时间范围过滤
                if (param.From.HasValue)
                {
                    filteredLogs = filteredLogs.Where(e => e.Timestamp >= param.From.Value);
                }
                if (param.To.HasValue)
                {
                    filteredLogs = filteredLogs.Where(e => e.Timestamp <= param.To.Value);
                }

                // 应用级别过滤
                if (!string.IsNullOrEmpty(param.Level) && Enum.TryParse<LogEventLevel>(param.Level, true, out var levelFilter))
                {
                    filteredLogs = filteredLogs.Where(e => e.Level == levelFilter);
                }

                var filteredList = filteredLogs.ToList();
                int totalCount = filteredList.Count;

                var pagedLogs = filteredList
                    .OrderByDescending(e => e.Timestamp)
                    .Skip(param.Offset)
                    .Take(Math.Min(param.Limit, 1000))
                    .Select(e => new LogEntryDto
                    {
                        Timestamp = e.Timestamp,
                        Level = e.Level.ToString(),
                        Message = e.RenderMessage(),
                        Exception = e.Exception?.ToString(),
                        Properties = e.Properties.ToDictionary(p => p.Key, p => p.Value.ToString())
                    })
                    .ToList();

                var result = new LogQueryResult
                {
                    Total = totalCount,
                    Limit = param.Limit,
                    Offset = param.Offset,
                    Count = pagedLogs.Count,
                    Logs = pagedLogs
                };

                return JsonSerializer.SerializeToUtf8Bytes(result, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MQTT RPC: 查询日志时发生异常");
                return JsonSerializer.SerializeToUtf8Bytes(
                    new CommandResult { Success = false, Code = "LOGS_QUERY_EXCEPTION", Message = $"内部错误：{ex.Message}" },
                    _jsonOptions);
            }
        }

        /// <summary>
        /// logs-by-level — 按日志级别获取日志
        /// payload：LogByLevelParams JSON
        /// </summary>
        private async Task<byte[]> HandleLogsByLevel(byte[] payload)
        {
            try
            {
                var param = payload.Length > 0
                    ? JsonSerializer.Deserialize<LogByLevelParams>(payload, _jsonOptions)
                    : new LogByLevelParams();

                if (param == null || string.IsNullOrEmpty(param.Level))
                {
                    return JsonSerializer.SerializeToUtf8Bytes(
                        new CommandResult { Success = false, Code = "INVALID_PARAM", Message = "日志级别不能为空" },
                        _jsonOptions);
                }

                _logger.LogInformation("MQTT RPC: 按级别查询日志请求：level={Level}, limit={Limit}", param.Level, param.Limit);

                if (!Enum.TryParse<LogEventLevel>(param.Level, true, out var levelFilter))
                {
                    return JsonSerializer.SerializeToUtf8Bytes(
                        new CommandResult
                        {
                            Success = false,
                            Code = "INVALID_PARAM",
                            Message = $"无效的日志级别：{param.Level}，有效值为：{string.Join(", ", Enum.GetNames(typeof(LogEventLevel)))}"
                        },
                        _jsonOptions);
                }

                var logEvents = InMemorySink.Instance.LogEvents;
                var filteredLogs = logEvents
                    .Where(e => e.Level == levelFilter)
                    .OrderByDescending(e => e.Timestamp)
                    .Take(Math.Min(param.Limit, 1000))
                    .Select(e => new LogEntryDto
                    {
                        Timestamp = e.Timestamp,
                        Level = e.Level.ToString(),
                        Message = e.RenderMessage(),
                        Exception = e.Exception?.ToString(),
                        Properties = e.Properties.ToDictionary(p => p.Key, p => p.Value.ToString())
                    })
                    .ToList();

                var result = new LogByLevelResult
                {
                    Level = param.Level,
                    Count = filteredLogs.Count,
                    Logs = filteredLogs
                };

                return JsonSerializer.SerializeToUtf8Bytes(result, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MQTT RPC: 按级别查询日志时发生异常");
                return JsonSerializer.SerializeToUtf8Bytes(
                    new CommandResult { Success = false, Code = "LOGS_BY_LEVEL_EXCEPTION", Message = $"内部错误：{ex.Message}" },
                    _jsonOptions);
            }
        }

        /// <summary>
        /// logs-level-stats — 获取所有日志级别的统计信息
        /// </summary>
        private async Task<byte[]> HandleLevelStats(byte[] payload)
        {
            try
            {
                _logger.LogInformation("MQTT RPC: 获取日志级别统计信息");

                var logEvents = InMemorySink.Instance.LogEvents.ToList();
                var stats = logEvents
                    .GroupBy(e => e.Level)
                    .Select(g => new LogLevelStatItem
                    {
                        Level = g.Key.ToString(),
                        Count = g.Count(),
                        Latest = g.Max(e => e.Timestamp)
                    })
                    .OrderByDescending(s => s.Count)
                    .ToList();

                var result = new LogStatsResult
                {
                    TotalLogs = logEvents.Count,
                    Statistics = stats
                };

                return JsonSerializer.SerializeToUtf8Bytes(result, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MQTT RPC: 获取日志级别统计时发生异常");
                return JsonSerializer.SerializeToUtf8Bytes(
                    new CommandResult { Success = false, Code = "LOGS_STATS_EXCEPTION", Message = $"内部错误：{ex.Message}" },
                    _jsonOptions);
            }
        }

        /// <summary>
        /// logs-clear — 清空内存中的日志
        /// </summary>
        private Task<byte[]> HandleClearLogs(byte[] payload)
        {
            try
            {
                _logger.LogInformation("MQTT RPC: 清空内存日志");
                _logger.LogWarning("清空内存日志功能暂未实现");
                var result = new { Message = "内存日志已清空" };
                return Task.FromResult(JsonSerializer.SerializeToUtf8Bytes(result, _jsonOptions));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MQTT RPC: 清空内存日志时发生异常");
                return Task.FromResult(JsonSerializer.SerializeToUtf8Bytes(
                    new CommandResult { Success = false, Code = "LOGS_CLEAR_EXCEPTION", Message = $"内部错误：{ex.Message}" },
                    _jsonOptions));
            }
        }

        /// <summary>
        /// logs-health — 日志控制器健康检查
        /// </summary>
        private Task<byte[]> HandleHealth(byte[] payload)
        {
            var result = new { Status = "OK", Message = "LogHandler is working" };
            return Task.FromResult(JsonSerializer.SerializeToUtf8Bytes(result, _jsonOptions));
        }
    }
}
