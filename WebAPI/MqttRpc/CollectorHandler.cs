using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WebAPI.Models;
using WebAPI.Service;
using WebAPI.Tools;

namespace WebAPI.MqttRpc
{
    /// <summary>
    /// 采集卡领域 MQTT RPC Handler
    /// 替代 ClientController 的 HTTP 端点，直接调用共享服务层
    /// 13 个 RPC 方法与 HTTP 端点一一对应
    /// 方法名使用 "-" 分隔以避免与 MQTT 主题层级 "/" 冲突
    /// </summary>
    public class CollectorHandler
    {
        /// <summary>
        /// 硬编码的采集子进程客户端标识
        /// </summary>
        private const string CollectorClientId = "数据采集子进程";

        private readonly GrpcServiceImpl _grpcService;
        private readonly ConfigHelper _configHelper;
        private readonly SystemStateService _systemStateService;
        private readonly ILogger<CollectorHandler> _logger;

        /// <summary>
        /// JSON 序列化选项（紧凑格式）
        /// </summary>
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>
        /// 构造函数，注入 gRPC 服务、配置助手、系统状态服务和日志器
        /// </summary>
        public CollectorHandler(
            GrpcServiceImpl grpcService,
            ConfigHelper configHelper,
            SystemStateService systemStateService,
            ILogger<CollectorHandler> logger)
        {
            _grpcService = grpcService;
            _configHelper = configHelper;
            _systemStateService = systemStateService;
            _logger = logger;
        }

        /// <summary>
        /// 获取所有 RPC 方法名到处理函数的映射字典
        /// 由 MqttRpcBackgroundService 调用，用于注册 MQTT 消息路由
        /// </summary>
        /// <returns>方法名 → 处理函数 的字典</returns>
        public Dictionary<string, Func<byte[], Task<byte[]>>> GetHandlers()
        {
            return new Dictionary<string, Func<byte[], Task<byte[]>>>
            {
                ["collector-status"] = HandleStatus,
                ["collector-command-send"] = HandleSendCommand,
                ["collector-command-send-async"] = HandleSendCommandAsync,
                ["collector-open-device"] = HandleOpenDevice,
                ["collector-open-device-again"] = HandleOpenDeviceAgain,
                ["collector-close-device"] = HandleCloseDevice,
                ["collector-start-ad"] = HandleStartAd,
                ["collector-stop-ad"] = HandleStopAd,
                ["collector-ping"] = HandlePing,
                ["collector-exit"] = HandleExit,
                ["collector-config-read"] = HandleConfigRead,
                ["collector-config-update"] = HandleConfigUpdate,
                ["collector-config-default"] = HandleConfigDefault,
            };
        }

        /// <summary>
        /// collector-status — 检查采集子进程是否已连接
        /// </summary>
        private Task<byte[]> HandleStatus(byte[] payload)
        {
            try
            {
                bool isConnected = IsClientConnected();
                var result = new
                {
                    ClientId = CollectorClientId,
                    Connected = isConnected,
                    Timestamp = DateTime.Now
                };
                return Task.FromResult(JsonSerializer.SerializeToUtf8Bytes(result, _jsonOptions));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MQTT RPC: 检查采集子进程连接状态时发生异常");
                var result = new CommandResult
                {
                    Success = false,
                    Code = "COLLECTOR_STATUS_EXCEPTION",
                    Message = $"内部错误：{ex.Message}"
                };
                return Task.FromResult(JsonSerializer.SerializeToUtf8Bytes(result, _jsonOptions));
            }
        }

        /// <summary>
        /// collector-command-send — 向采集子进程发送指令并等待响应
        /// payload：JSON 序列化的 string 命令内容
        /// </summary>
        private async Task<byte[]> HandleSendCommand(byte[] payload)
        {
            try
            {
                var command = payload.Length > 0
                    ? JsonSerializer.Deserialize<string>(payload, _jsonOptions)
                    : null;

                if (string.IsNullOrEmpty(command))
                {
                    return JsonSerializer.SerializeToUtf8Bytes(
                        new CommandResult { Success = false, Code = "INVALID_PARAM", Message = "指令不能为空" },
                        _jsonOptions);
                }

                _logger.LogInformation("MQTT RPC: 向采集子进程发送指令：{Command}", command);
                var response = await _grpcService.SendCommandToClientAndWaitResponse(CollectorClientId, command);
                return JsonSerializer.SerializeToUtf8Bytes(response, _jsonOptions);
            }
            catch (Exception ex) when (ex.Message.Contains("未连接"))
            {
                _logger.LogWarning("MQTT RPC: 采集子进程未连接");
                return JsonSerializer.SerializeToUtf8Bytes(
                    new CommandResult { Success = false, Code = "COLLECTOR_NOT_CONNECTED", Message = "采集子进程未连接" },
                    _jsonOptions);
            }
            catch (TimeoutException ex)
            {
                _logger.LogWarning("MQTT RPC: 等待采集子进程响应超时");
                return JsonSerializer.SerializeToUtf8Bytes(
                    new CommandResult { Success = false, Code = "COLLECTOR_TIMEOUT", Message = $"等待响应超时：{ex.Message}" },
                    _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MQTT RPC: 向采集子进程发送指令时发生异常");
                return JsonSerializer.SerializeToUtf8Bytes(
                    new CommandResult { Success = false, Code = "COLLECTOR_COMMAND_EXCEPTION", Message = $"内部错误：{ex.Message}" },
                    _jsonOptions);
            }
        }

        /// <summary>
        /// collector-command-send-async — 向采集子进程发送指令（异步，不等待）
        /// payload：JSON 序列化的 string 命令内容
        /// </summary>
        private async Task<byte[]> HandleSendCommandAsync(byte[] payload)
        {
            try
            {
                var command = payload.Length > 0
                    ? JsonSerializer.Deserialize<string>(payload, _jsonOptions)
                    : null;

                if (string.IsNullOrEmpty(command))
                {
                    return JsonSerializer.SerializeToUtf8Bytes(
                        new CommandResult { Success = false, Code = "INVALID_PARAM", Message = "指令不能为空" },
                        _jsonOptions);
                }

                _logger.LogInformation("MQTT RPC: 向采集子进程异步发送指令：{Command}", command);
                await _grpcService.SendCommandToClient(CollectorClientId, command);
                return JsonSerializer.SerializeToUtf8Bytes(
                    new { Accepted = true }, _jsonOptions);
            }
            catch (Exception ex) when (ex.Message.Contains("未连接"))
            {
                _logger.LogWarning("MQTT RPC: 采集子进程未连接");
                return JsonSerializer.SerializeToUtf8Bytes(
                    new CommandResult { Success = false, Code = "COLLECTOR_NOT_CONNECTED", Message = "采集子进程未连接" },
                    _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MQTT RPC: 向采集子进程异步发送指令时发生异常");
                return JsonSerializer.SerializeToUtf8Bytes(
                    new CommandResult { Success = false, Code = "COLLECTOR_COMMAND_EXCEPTION", Message = $"内部错误：{ex.Message}" },
                    _jsonOptions);
            }
        }

        /// <summary>
        /// collector-open-device — 发送打开采集卡设备指令
        /// </summary>
        private async Task<byte[]> HandleOpenDevice(byte[] payload)
        {
            try
            {
                var response = await _grpcService.SendCommandToClientAndWaitResponse(CollectorClientId, "OPEN_DEVICE");
                var state = _systemStateService.Get_System_State_Struct();
                var result = new CommandResult
                {
                    Success = state.Collector.DeviceOpened,
                    Code = state.Collector.DeviceOpened ? "COLLECTOR_OPENED" : "COLLECTOR_OPEN_FAILED",
                    Message = response.Content
                };
                return JsonSerializer.SerializeToUtf8Bytes(result, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MQTT RPC: 打开采集卡时发生异常");
                var result = new CommandResult
                {
                    Success = false,
                    Code = "COLLECTOR_OPEN_EXCEPTION",
                    Message = $"打开采集卡时发生异常：{ex.Message}"
                };
                return JsonSerializer.SerializeToUtf8Bytes(result, _jsonOptions);
            }
        }

        /// <summary>
        /// collector-open-device-again — 发送重新打开采集卡设备指令
        /// </summary>
        private async Task<byte[]> HandleOpenDeviceAgain(byte[] payload)
        {
            try
            {
                var response = await _grpcService.SendCommandToClientAndWaitResponse(CollectorClientId, "OPEN_DEVICE_AGAIN");
                var state = _systemStateService.Get_System_State_Struct();
                var result = new CommandResult
                {
                    Success = state.Collector.DeviceOpened,
                    Code = state.Collector.DeviceOpened ? "COLLECTOR_OPENED" : "COLLECTOR_OPEN_FAILED",
                    Message = response.Content
                };
                return JsonSerializer.SerializeToUtf8Bytes(result, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MQTT RPC: 重新打开采集卡时发生异常");
                var result = new CommandResult
                {
                    Success = false,
                    Code = "COLLECTOR_OPEN_EXCEPTION",
                    Message = $"重新打开采集卡时发生异常：{ex.Message}"
                };
                return JsonSerializer.SerializeToUtf8Bytes(result, _jsonOptions);
            }
        }

        /// <summary>
        /// collector-close-device — 发送关闭采集卡设备指令
        /// </summary>
        private async Task<byte[]> HandleCloseDevice(byte[] payload)
        {
            try
            {
                var response = await _grpcService.SendCommandToClientAndWaitResponse(CollectorClientId, "CLOSE_DEVICE");
                var state = _systemStateService.Get_System_State_Struct();
                var result = new CommandResult
                {
                    Success = !state.Collector.DeviceOpened,
                    Code = !state.Collector.DeviceOpened ? "COLLECTOR_CLOSED" : "COLLECTOR_CLOSE_FAILED",
                    Message = response.Content
                };
                return JsonSerializer.SerializeToUtf8Bytes(result, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MQTT RPC: 关闭采集卡时发生异常");
                var result = new CommandResult
                {
                    Success = false,
                    Code = "COLLECTOR_CLOSE_EXCEPTION",
                    Message = $"关闭采集卡时发生异常：{ex.Message}"
                };
                return JsonSerializer.SerializeToUtf8Bytes(result, _jsonOptions);
            }
        }

        /// <summary>
        /// collector-start-ad — 发送开始采集指令
        /// </summary>
        private async Task<byte[]> HandleStartAd(byte[] payload)
        {
            try
            {
                var response = await _grpcService.SendCommandToClientAndWaitResponse(CollectorClientId, "START_AD");
                var state = _systemStateService.Get_System_State_Struct();
                var result = new CommandResult
                {
                    Success = state.Collector.Acquiring,
                    Code = state.Collector.Acquiring ? "AD_STARTED" : "AD_START_FAILED",
                    Message = response.Content
                };
                return JsonSerializer.SerializeToUtf8Bytes(result, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MQTT RPC: 开始采集时发生异常");
                var result = new CommandResult
                {
                    Success = false,
                    Code = "AD_START_EXCEPTION",
                    Message = $"开始采集时发生异常：{ex.Message}"
                };
                return JsonSerializer.SerializeToUtf8Bytes(result, _jsonOptions);
            }
        }

        /// <summary>
        /// collector-stop-ad — 发送停止采集指令
        /// </summary>
        private async Task<byte[]> HandleStopAd(byte[] payload)
        {
            try
            {
                var response = await _grpcService.SendCommandToClientAndWaitResponse(CollectorClientId, "STOP_AD");
                var state = _systemStateService.Get_System_State_Struct();
                var result = new CommandResult
                {
                    Success = !state.Collector.Acquiring,
                    Code = !state.Collector.Acquiring ? "AD_STOPPED" : "AD_STOP_FAILED",
                    Message = response.Content
                };
                return JsonSerializer.SerializeToUtf8Bytes(result, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MQTT RPC: 停止采集时发生异常");
                var result = new CommandResult
                {
                    Success = false,
                    Code = "AD_STOP_EXCEPTION",
                    Message = $"停止采集时发生异常：{ex.Message}"
                };
                return JsonSerializer.SerializeToUtf8Bytes(result, _jsonOptions);
            }
        }

        /// <summary>
        /// collector-ping — 发送心跳检测指令（PING 命令）
        /// </summary>
        private async Task<byte[]> HandlePing(byte[] payload)
        {
            // 构造 PING 命令的 JSON payload 委托给 HandleSendCommand
            var pingPayload = JsonSerializer.SerializeToUtf8Bytes("PING", _jsonOptions);
            return await HandleSendCommand(pingPayload);
        }

        /// <summary>
        /// collector-exit — 发送优雅退出指令（EXIT 命令）
        /// </summary>
        private async Task<byte[]> HandleExit(byte[] payload)
        {
            var exitPayload = JsonSerializer.SerializeToUtf8Bytes("EXIT", _jsonOptions);
            return await HandleSendCommand(exitPayload);
        }

        /// <summary>
        /// collector-config-read — 读取采集卡配置文件并返回最新配置
        /// </summary>
        private async Task<byte[]> HandleConfigRead(byte[] payload)
        {
            try
            {
                await _grpcService.SendCommandToClientAndWaitResponse(CollectorClientId, "CONFIG_READ");
                _configHelper.ReadDeviceConfig();
                return JsonSerializer.SerializeToUtf8Bytes(Program.CurrentConfig, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MQTT RPC: 读取采集卡配置时发生异常");
                _configHelper.ReadDeviceConfig();
                return JsonSerializer.SerializeToUtf8Bytes(Program.CurrentConfig, _jsonOptions);
            }
        }

        /// <summary>
        /// collector-config-update — 更新采集卡配置
        /// payload：CaptureCardConfig JSON
        /// </summary>
        private async Task<byte[]> HandleConfigUpdate(byte[] payload)
        {
            try
            {
                var newConfig = payload.Length > 0
                    ? JsonSerializer.Deserialize<CaptureCardConfig>(payload, _jsonOptions)
                    : null;

                if (newConfig == null)
                {
                    return JsonSerializer.SerializeToUtf8Bytes(
                        new CommandResult { Success = false, Code = "INVALID_PARAM", Message = "配置不能为空" },
                        _jsonOptions);
                }

                // 写入配置文件
                _configHelper.WriteCaptureCardConfig(newConfig);

                // 通知采集子进程配置已更新
                try
                {
                    await _grpcService.SendCommandToClientAndWaitResponse(CollectorClientId, "CONFIG_UPDATE");
                }
                catch (Exception ex) when (ex.Message.Contains("未连接"))
                {
                    _logger.LogWarning("MQTT RPC: 采集子进程未连接，但配置已保存");
                }

                // 更新内存中的全局配置
                _configHelper.ReadDeviceConfig();
                _logger.LogInformation("MQTT RPC: 采集卡配置更新完成");
                return JsonSerializer.SerializeToUtf8Bytes(Program.CurrentConfig, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MQTT RPC: 更新采集卡配置时发生异常");
                return JsonSerializer.SerializeToUtf8Bytes(
                    new CommandResult { Success = false, Code = "CONFIG_UPDATE_EXCEPTION", Message = $"内部错误：{ex.Message}" },
                    _jsonOptions);
            }
        }

        /// <summary>
        /// collector-config-default — 获取默认采集卡配置
        /// </summary>
        private Task<byte[]> HandleConfigDefault(byte[] payload)
        {
            try
            {
                _logger.LogInformation("MQTT RPC: 获取默认采集卡配置");
                var defaultConfig = new CaptureCardConfig();
                return Task.FromResult(JsonSerializer.SerializeToUtf8Bytes(defaultConfig, _jsonOptions));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MQTT RPC: 获取默认配置时发生异常");
                var result = new CommandResult
                {
                    Success = false,
                    Code = "CONFIG_DEFAULT_EXCEPTION",
                    Message = $"内部错误：{ex.Message}"
                };
                return Task.FromResult(JsonSerializer.SerializeToUtf8Bytes(result, _jsonOptions));
            }
        }

        /// <summary>
        /// 检查采集子进程是否在连接池中
        /// </summary>
        private bool IsClientConnected()
        {
            try
            {
                var clients = _grpcService.GetConnectedClients();
                return Array.Exists(clients, id => id == CollectorClientId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查客户端连接状态时发生异常");
                return false;
            }
        }
    }
}
