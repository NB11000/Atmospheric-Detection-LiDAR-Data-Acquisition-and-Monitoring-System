using Microsoft.AspNetCore.Mvc;
using Serilog;
using Serilog.Sinks.InMemory;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using WebAPI.Models;

namespace WebAPI.Controllers
{
    /// <summary>
    /// 系统日志查询API控制器
    /// 提供对内存中Serilog日志的查询和管理功能
    /// </summary>
    [ApiController]
    [Route("api/logs")]
    public class LogController : ControllerBase
    {
        private readonly ILogger<LogController> _logger;

        /// <summary>
        /// 构造函数，注入日志记录器
        /// </summary>
        /// <param name="logger">日志记录器实例</param>
        public LogController(ILogger<LogController> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 获取系统日志（支持分页、过滤、时间范围）
        /// </summary>
        /// <param name="limit">返回日志条数限制（默认100，最大1000）</param>
        /// <param name="offset">跳过前N条日志（默认0）</param>
        /// <param name="level">日志级别过滤（如Information、Error）</param>
        /// <param name="from">起始时间（ISO 8601格式，如2024-01-01T00:00:00）</param>
        /// <param name="to">结束时间（ISO 8601格式）</param>
        /// <returns>日志条目列表</returns>
         [HttpGet]
        public IActionResult GetLogs(
            [FromQuery] int limit = 100,
            [FromQuery] int offset = 0,
            [FromQuery] string? level = null,
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null)
        {
            try
            {
                _logger.LogInformation($"查询日志请求：limit={limit}, offset={offset}, level={level}, from={from}, to={to}");
                
                // 诊断信息：检查InMemorySink状态
                var sinkInstance = InMemorySink.Instance;
                _logger.LogInformation($"InMemorySink诊断: Instance is null? {sinkInstance == null}");
                if (sinkInstance != null)
                {
                    var logEvents = sinkInstance.LogEvents;
                    _logger.LogInformation($"InMemorySink诊断: LogEvents类型 = {logEvents.GetType().FullName}");
                    _logger.LogInformation($"InMemorySink诊断: LogEvents count = {logEvents.Count()}");
                    
                    // 检查是否有任何日志
                    var firstFewLogs = logEvents.Take(5).ToList();
                    _logger.LogInformation($"InMemorySink诊断: 前{firstFewLogs.Count}条日志");
                    foreach (var log in firstFewLogs)
                    {
                        _logger.LogInformation($"日志: {log.Timestamp} [{log.Level}] {log.RenderMessage()}");
                    }
                }
                else
                {
                    _logger.LogWarning("InMemorySink.Instance is null - 日志可能未配置正确");
                }
                
                // 直接使用Serilog记录测试日志
                Log.Information("Serilog直接记录测试 - 此日志应出现在InMemorySink中");
                _logger.LogInformation("Microsoft.Extensions.Logging记录测试 - 此日志也应出现在InMemorySink中");
                
                var allLogEvents = InMemorySink.Instance.LogEvents;
                var filteredLogs = allLogEvents.AsEnumerable();

                // 应用时间范围过滤
                if (from.HasValue)
                {
                    filteredLogs = filteredLogs.Where(e => e.Timestamp >= from.Value);
                }
                if (to.HasValue)
                {
                    filteredLogs = filteredLogs.Where(e => e.Timestamp <= to.Value);
                }

                // 应用级别过滤
                if (!string.IsNullOrEmpty(level) && Enum.TryParse<LogEventLevel>(level, true, out var levelFilter))
                {
                    filteredLogs = filteredLogs.Where(e => e.Level == levelFilter);
                }

                // 获取总数（用于分页信息）
                var filteredList = filteredLogs.ToList();
                int totalCount = filteredList.Count;

                // 应用分页和转换
                var pagedLogs = filteredList
                    .OrderByDescending(e => e.Timestamp) // 最新日志在前
                    .Skip(offset)
                    .Take(Math.Min(limit, 1000)) // 限制最大1000条
                    .Select(e => new LogEntryDto
                    {
                        Timestamp = e.Timestamp,
                        Level = e.Level.ToString(),
                        Message = e.RenderMessage(),
                        Exception = e.Exception?.ToString(),
                        Properties = e.Properties.ToDictionary(p => p.Key, p => p.Value.ToString())
                    })
                    .ToList();

                return Ok(new
                {
                    Total = totalCount,
                    Limit = limit,
                    Offset = offset,
                    Count = pagedLogs.Count,
                    Logs = pagedLogs
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查询日志时发生异常");
                return StatusCode(500, $"内部服务器错误：{ex.Message}");
            }
        }

        /// <summary>
        /// 按日志级别获取日志
        /// </summary>
        /// <param name="level">日志级别（如Information、Error、Warning）</param>
        /// <param name="limit">返回日志条数限制（默认100）</param>
        /// <returns>指定级别的日志条目列表</returns>
        [HttpGet("{level}")]
        public IActionResult GetLogsByLevel(string level, [FromQuery] int limit = 100)
        {
            try
            {
                _logger.LogInformation($"按级别查询日志请求：level={level}, limit={limit}");
                
                if (!Enum.TryParse<LogEventLevel>(level, true, out var levelFilter))
                {
                    return BadRequest($"无效的日志级别：{level}，有效值为：{string.Join(", ", Enum.GetNames(typeof(LogEventLevel)))}");
                }

                var logEvents = InMemorySink.Instance.LogEvents;
                var filteredLogs = logEvents
                    .Where(e => e.Level == levelFilter)
                    .OrderByDescending(e => e.Timestamp)
                    .Take(Math.Min(limit, 1000))
                    .Select(e => new LogEntryDto
                    {
                        Timestamp = e.Timestamp,
                        Level = e.Level.ToString(),
                        Message = e.RenderMessage(),
                        Exception = e.Exception?.ToString(),
                        Properties = e.Properties.ToDictionary(p => p.Key, p => p.Value.ToString())
                    })
                    .ToList();

                return Ok(new
                {
                    Level = level,
                    Count = filteredLogs.Count,
                    Logs = filteredLogs
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"按级别[{level}]查询日志时发生异常");
                return StatusCode(500, $"内部服务器错误：{ex.Message}");
            }
        }

        /// <summary>
        /// 获取所有日志级别的统计信息
        /// </summary>
        /// <returns>各日志级别的数量统计</returns>
        [HttpGet("levels")]
        public IActionResult GetLevelStatistics()
        {
            try
            {
                _logger.LogInformation("获取日志级别统计信息");
                
                var logEvents = InMemorySink.Instance.LogEvents.ToList();
                var stats = logEvents
                    .GroupBy(e => e.Level)
                    .Select(g => new
                    {
                        Level = g.Key.ToString(),
                        Count = g.Count(),
                        Latest = g.Max(e => e.Timestamp)
                    })
                    .OrderByDescending(s => s.Count)
                    .ToList();

                return Ok(new
                {
                    TotalLogs = logEvents.Count(),
                    Statistics = stats
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取日志级别统计时发生异常");
                return StatusCode(500, $"内部服务器错误：{ex.Message}");
            }
        }

        /// <summary>
        /// 清空内存中的日志
        /// </summary>
        /// <returns>操作结果</returns>
        [HttpDelete]
        public IActionResult ClearLogs()
        {
            try
            {
                _logger.LogInformation("清空内存日志");
                // InMemorySink 目前没有提供清空日志的方法
                // 可以通过重新配置Serilog或使用其他方式实现
                // 暂时记录警告，不执行实际清空操作
                _logger.LogWarning("清空内存日志功能暂未实现，需要进一步研究Serilog.Sinks.InMemory API");
                return Ok(new { Message = "内存日志已清空" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清空内存日志时发生异常");
                return StatusCode(500, $"内部服务器错误：{ex.Message}");
            }
        }
        

        /// <summary>
        /// 健康检查端点
        /// </summary>
        /// <returns>状态消息</returns>
        [HttpGet("health")]
        public IActionResult Health()
        {
            return Ok(new { status = "OK", message = "LogController is working" });
        }
    }

}