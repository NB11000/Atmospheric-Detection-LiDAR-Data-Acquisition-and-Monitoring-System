using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SharedModels;
using WebAPI.Tools;

namespace WebAPI.MqttRpc
{
    /// <summary>
    /// 统一配置领域 MQTT RPC Handler
    /// 管理 LidarAlgorithm、Persistence 等非采集卡/激光器的配置读写
    /// 4 个 RPC 方法
    /// </summary>
    public class ConfigHandler
    {
        private readonly ConfigHelper _configHelper;
        private readonly ILogger<ConfigHandler> _logger;

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public ConfigHandler(ConfigHelper configHelper, ILogger<ConfigHandler> logger)
        {
            _configHelper = configHelper;
            _logger = logger;
        }

        public Dictionary<string, Func<byte[], Task<byte[]>>> GetHandlers()
        {
            return new Dictionary<string, Func<byte[], Task<byte[]>>>
            {
                ["lidar-config-read"] = HandleLidarConfigRead,
                ["lidar-config-update"] = HandleLidarConfigUpdate,
                ["persistence-config-read"] = HandlePersistenceConfigRead,
                ["persistence-config-update"] = HandlePersistenceConfigUpdate,
            };
        }

        private Task<byte[]> HandleLidarConfigRead(byte[] payload)
        {
            try
            {
                _configHelper.ReadLidarConfig();
                return Task.FromResult(JsonSerializer.SerializeToUtf8Bytes(Program.LidarConfig, _jsonOptions));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MQTT RPC: 读取反演算法配置时发生异常");
                var result = new CommandResult
                {
                    Success = false,
                    Code = "LIDAR_CONFIG_READ_EXCEPTION",
                    Message = $"内部错误：{ex.Message}"
                };
                return Task.FromResult(JsonSerializer.SerializeToUtf8Bytes(result, _jsonOptions));
            }
        }

        private async Task<byte[]> HandleLidarConfigUpdate(byte[] payload)
        {
            try
            {
                var newConfig = payload.Length > 0
                    ? JsonSerializer.Deserialize<LidarAlgorithmConfig>(payload, _jsonOptions)
                    : null;

                if (newConfig == null)
                {
                    return JsonSerializer.SerializeToUtf8Bytes(
                        new CommandResult { Success = false, Code = "INVALID_PARAM", Message = "配置不能为空" },
                        _jsonOptions);
                }

                _configHelper.WriteLidarConfig(newConfig);
                _configHelper.ReadLidarConfig();
                _logger.LogInformation("MQTT RPC: 反演算法配置更新完成");
                return JsonSerializer.SerializeToUtf8Bytes(Program.LidarConfig, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MQTT RPC: 更新反演算法配置时发生异常");
                return JsonSerializer.SerializeToUtf8Bytes(
                    new CommandResult { Success = false, Code = "LIDAR_CONFIG_UPDATE_EXCEPTION", Message = $"内部错误：{ex.Message}" },
                    _jsonOptions);
            }
        }

        private Task<byte[]> HandlePersistenceConfigRead(byte[] payload)
        {
            try
            {
                _configHelper.ReadPersistenceConfig();
                return Task.FromResult(JsonSerializer.SerializeToUtf8Bytes(Program.PersistenceConfig, _jsonOptions));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MQTT RPC: 读取持久化配置时发生异常");
                var result = new CommandResult
                {
                    Success = false,
                    Code = "PERSISTENCE_CONFIG_READ_EXCEPTION",
                    Message = $"内部错误：{ex.Message}"
                };
                return Task.FromResult(JsonSerializer.SerializeToUtf8Bytes(result, _jsonOptions));
            }
        }

        private async Task<byte[]> HandlePersistenceConfigUpdate(byte[] payload)
        {
            try
            {
                var newConfig = payload.Length > 0
                    ? JsonSerializer.Deserialize<PersistenceSettings>(payload, _jsonOptions)
                    : null;

                if (newConfig == null)
                {
                    return JsonSerializer.SerializeToUtf8Bytes(
                        new CommandResult { Success = false, Code = "INVALID_PARAM", Message = "配置不能为空" },
                        _jsonOptions);
                }

                _configHelper.WritePersistenceConfig(newConfig);
                _configHelper.ReadPersistenceConfig();
                _logger.LogInformation("MQTT RPC: 持久化配置更新完成");
                return JsonSerializer.SerializeToUtf8Bytes(Program.PersistenceConfig, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MQTT RPC: 更新持久化配置时发生异常");
                return JsonSerializer.SerializeToUtf8Bytes(
                    new CommandResult { Success = false, Code = "PERSISTENCE_CONFIG_UPDATE_EXCEPTION", Message = $"内部错误：{ex.Message}" },
                    _jsonOptions);
            }
        }
    }
}
