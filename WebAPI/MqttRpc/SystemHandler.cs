using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WebAPI.Models;
using WebAPI.Service;

namespace WebAPI.MqttRpc
{
    /// <summary>
    /// 系统状态领域 MQTT RPC Handler
    /// 替代 SystemStateController 的 HTTP 端点，直接调用共享服务层
    /// 1 个 RPC 方法与 HTTP 端点一一对应
    /// </summary>
    public class SystemHandler
    {
        private readonly SystemStateService _systemStateService;
        private readonly ILogger<SystemHandler> _logger;

        /// <summary>
        /// JSON 序列化选项（紧凑格式）
        /// </summary>
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>
        /// 构造函数，注入系统状态服务
        /// </summary>
        public SystemHandler(SystemStateService systemStateService, ILogger<SystemHandler> logger)
        {
            _systemStateService = systemStateService;
            _logger = logger;
        }

        /// <summary>
        /// 获取所有 RPC 方法名到处理函数的映射字典
        /// </summary>
        public Dictionary<string, Func<byte[], Task<byte[]>>> GetHandlers()
        {
            return new Dictionary<string, Func<byte[], Task<byte[]>>>
            {
                ["system-state"] = HandleGetSystemState,
            };
        }

        /// <summary>
        /// system-state — 获取系统统一状态快照
        /// </summary>
        private async Task<byte[]> HandleGetSystemState(byte[] payload)
        {
            try
            {
                var state = await _systemStateService.GetSystemStateAsync();
                return JsonSerializer.SerializeToUtf8Bytes(state, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MQTT RPC: 获取系统状态时发生异常");
                var result = new CommandResult
                {
                    Success = false,
                    Code = "SYSTEM_STATE_EXCEPTION",
                    Message = $"内部错误：{ex.Message}"
                };
                return JsonSerializer.SerializeToUtf8Bytes(result, _jsonOptions);
            }
        }
    }
}
