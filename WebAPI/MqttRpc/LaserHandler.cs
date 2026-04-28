using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using CniLaserControl;
using Microsoft.Extensions.Logging;
using WebAPI.Models;
using WebAPI.Service;
using WebAPI.Tools;

namespace WebAPI.MqttRpc
{
    /// <summary>
    /// 激光器领域 MQTT RPC Handler
    /// 替代 LaserController 的 HTTP 端点，直接调用共享服务层
    /// 7 个 RPC 方法与 HTTP 端点一一对应
    /// </summary>
    public class LaserHandler
    {
        private readonly CniLaser _laser;
        private readonly ConfigHelper _configHelper;
        private readonly SystemStateService _systemStateService;
        private readonly ILogger<LaserHandler> _logger;

        /// <summary>
        /// JSON 序列化选项（紧凑格式）
        /// </summary>
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>
        /// 构造函数，注入激光器服务、配置助手、系统状态服务和日志器
        /// </summary>
        public LaserHandler(
            CniLaser laser,
            ConfigHelper configHelper,
            SystemStateService systemStateService,
            ILogger<LaserHandler> logger)
        {
            _laser = laser;
            _configHelper = configHelper;
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
                ["laser-connect"] = HandleConnect,
                ["laser-disconnect"] = HandleDisconnect,
                ["laser-on"] = HandleLaserOn,
                ["laser-off"] = HandleLaserOff,
                ["laser-status"] = HandleStatus,
                ["laser-config-update"] = HandleConfigUpdate,
                ["laser-config-read"] = HandleConfigRead,
            };
        }

        /// <summary>
        /// laser-connect — 打开串口连接激光器
        /// </summary>
        private async Task<byte[]> HandleConnect(byte[] payload)
        {
            try
            {
                var radarConfig = Program.RadarConfig;

                if (string.IsNullOrEmpty(radarConfig.SerialPort))
                {
                    var result = new CommandResult
                    {
                        Success = false,
                        Code = "LASER_CONNECT_FAILED",
                        Message = "串口号未配置，请先在配置中设置串口号"
                    };
                    return JsonSerializer.SerializeToUtf8Bytes(result, _jsonOptions);
                }

                _logger.LogInformation("MQTT RPC: 尝试打开串口连接激光器，端口：{Port}，波特率：{Baud}",
                    radarConfig.SerialPort, radarConfig.BaudRate);

                bool success = _laser.Connect(radarConfig.SerialPort, radarConfig.BaudRate);
                var stateAfter = _systemStateService.Get_System_State_Struct();

                var result2 = new CommandResult
                {
                    Success = stateAfter.Laser.SerialConnected,
                    Code = stateAfter.Laser.SerialConnected ? "LASER_CONNECTED" : "LASER_CONNECT_FAILED",
                    Message = stateAfter.Laser.SerialConnected ? "串口连接成功" : "串口连接失败，请检查端口和波特率设置"
                };
                return JsonSerializer.SerializeToUtf8Bytes(result2, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MQTT RPC: 打开串口连接激光器时发生异常");
                var result = new CommandResult
                {
                    Success = false,
                    Code = "LASER_CONNECT_EXCEPTION",
                    Message = $"打开串口连接激光器时发生异常：{ex.Message}"
                };
                return JsonSerializer.SerializeToUtf8Bytes(result, _jsonOptions);
            }
        }

        /// <summary>
        /// laser-disconnect — 断开串口连接
        /// </summary>
        private async Task<byte[]> HandleDisconnect(byte[] payload)
        {
            try
            {
                _logger.LogInformation("MQTT RPC: 断开串口连接");
                _laser.Disconnect();
                var state = _systemStateService.Get_System_State_Struct();
                var result = new CommandResult
                {
                    Success = !state.Laser.SerialConnected,
                    Code = !state.Laser.SerialConnected ? "LASER_DISCONNECTED" : "LASER_DISCONNECT_FAILED",
                    Message = !state.Laser.SerialConnected ? "串口已断开连接" : "串口断开连接失败"
                };
                return JsonSerializer.SerializeToUtf8Bytes(result, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MQTT RPC: 断开串口连接时发生异常");
                var result = new CommandResult
                {
                    Success = false,
                    Code = "LASER_DISCONNECT_EXCEPTION",
                    Message = $"断开串口连接时发生异常：{ex.Message}"
                };
                return JsonSerializer.SerializeToUtf8Bytes(result, _jsonOptions);
            }
        }

        /// <summary>
        /// laser-on — 开启激光（先设置功率和频率，再打开激光）
        /// </summary>
        private async Task<byte[]> HandleLaserOn(byte[] payload)
        {
            try
            {
                // 检查激光器是否已连接
                if (!_laser.IsConnected)
                {
                    var result = new CommandResult
                    {
                        Success = false,
                        Code = "LASER_ON_FAILED",
                        Message = "激光器未连接，请先连接激光器"
                    };
                    return JsonSerializer.SerializeToUtf8Bytes(result, _jsonOptions);
                }

                // 从全局配置中获取激光功率和调制频率
                var radarConfig = Program.RadarConfig;

                // 验证配置
                if (radarConfig.LaserPower <= 0)
                {
                    var result = new CommandResult
                    {
                        Success = false,
                        Code = "LASER_ON_FAILED",
                        Message = "激光功率未配置或配置无效，请先设置激光功率"
                    };
                    return JsonSerializer.SerializeToUtf8Bytes(result, _jsonOptions);
                }

                if (radarConfig.LaserModulationFrequency <= 0)
                {
                    var result = new CommandResult
                    {
                        Success = false,
                        Code = "LASER_ON_FAILED",
                        Message = "激光调制频率未配置或配置无效，请先设置调制频率"
                    };
                    return JsonSerializer.SerializeToUtf8Bytes(result, _jsonOptions);
                }

                _logger.LogInformation("MQTT RPC: 开始设置激光参数：功率={Power} mW，频率={Freq} Hz",
                    radarConfig.LaserPower, radarConfig.LaserModulationFrequency);

                // 1. 设置激光功率
                bool powerSuccess = _laser.SetPower(radarConfig.LaserPower);
                if (!powerSuccess)
                {
                    var result = new CommandResult
                    {
                        Success = false,
                        Code = "LASER_ON_FAILED",
                        Message = "激光功率设置失败，请检查激光器连接状态"
                    };
                    return JsonSerializer.SerializeToUtf8Bytes(result, _jsonOptions);
                }

                // 2. 设置激光调制频率
                bool frequencySuccess = _laser.SetFrequency(radarConfig.LaserModulationFrequency);
                if (!frequencySuccess)
                {
                    var result = new CommandResult
                    {
                        Success = false,
                        Code = "LASER_ON_FAILED",
                        Message = "激光调制频率设置失败，请检查激光器连接状态"
                    };
                    return JsonSerializer.SerializeToUtf8Bytes(result, _jsonOptions);
                }

                // 3. 开启激光
                _laser.LaserOn();
                var stateAfter = _systemStateService.Get_System_State_Struct();
                var result2 = new CommandResult
                {
                    Success = stateAfter.Laser.EmissionOn,
                    Code = stateAfter.Laser.EmissionOn ? "LASER_ON" : "LASER_ON_FAILED",
                    Message = stateAfter.Laser.EmissionOn ? "激光开启成功" : "激光开启失败"
                };
                return JsonSerializer.SerializeToUtf8Bytes(result2, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MQTT RPC: 开启激光时发生异常");
                var result = new CommandResult
                {
                    Success = false,
                    Code = "LASER_ON_EXCEPTION",
                    Message = $"开启激光时发生异常：{ex.Message}"
                };
                return JsonSerializer.SerializeToUtf8Bytes(result, _jsonOptions);
            }
        }

        /// <summary>
        /// laser-off — 关闭激光
        /// </summary>
        private async Task<byte[]> HandleLaserOff(byte[] payload)
        {
            try
            {
                _logger.LogInformation("MQTT RPC: 关闭激光");
                _laser.LaserOff();
                var state = _systemStateService.Get_System_State_Struct();
                var result = new CommandResult
                {
                    Success = !state.Laser.EmissionOn,
                    Code = !state.Laser.EmissionOn ? "LASER_OFF" : "LASER_OFF_FAILED",
                    Message = !state.Laser.EmissionOn ? "激光已关闭" : "激光关闭失败"
                };
                return JsonSerializer.SerializeToUtf8Bytes(result, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MQTT RPC: 关闭激光时发生异常");
                var result = new CommandResult
                {
                    Success = false,
                    Code = "LASER_OFF_EXCEPTION",
                    Message = $"关闭激光时发生异常：{ex.Message}"
                };
                return JsonSerializer.SerializeToUtf8Bytes(result, _jsonOptions);
            }
        }

        /// <summary>
        /// laser-status — 检查激光器连接状态
        /// </summary>
        private Task<byte[]> HandleStatus(byte[] payload)
        {
            try
            {
                var result = new
                {
                    Connected = _laser.IsConnected,
                    EmissionOn = _laser.IsEmissionOn,
                    PortName = _laser.PortName,
                    Timestamp = DateTime.Now
                };
                return Task.FromResult(JsonSerializer.SerializeToUtf8Bytes(result, _jsonOptions));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MQTT RPC: 检查激光器连接状态时发生异常");
                var result = new CommandResult
                {
                    Success = false,
                    Code = "LASER_STATUS_EXCEPTION",
                    Message = $"内部错误：{ex.Message}"
                };
                return Task.FromResult(JsonSerializer.SerializeToUtf8Bytes(result, _jsonOptions));
            }
        }

        /// <summary>
        /// laser-config-update — 更新激光雷达配置
        /// payload：RadarConfig JSON
        /// </summary>
        private async Task<byte[]> HandleConfigUpdate(byte[] payload)
        {
            try
            {
                var newConfig = payload.Length > 0
                    ? JsonSerializer.Deserialize<RadarConfig>(payload, _jsonOptions)
                    : null;

                if (newConfig == null)
                {
                    return JsonSerializer.SerializeToUtf8Bytes(
                        new CommandResult { Success = false, Code = "INVALID_PARAM", Message = "配置不能为空" },
                        _jsonOptions);
                }

                // 写入配置文件
                _configHelper.WriteRadarConfig(newConfig);

                // 更新内存中的全局配置
                _configHelper.ReadRadarDeviceConfig();

                _logger.LogInformation("MQTT RPC: 激光雷达配置更新完成");
                return JsonSerializer.SerializeToUtf8Bytes(Program.RadarConfig, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MQTT RPC: 更新激光雷达配置时发生异常");
                return JsonSerializer.SerializeToUtf8Bytes(
                    new CommandResult { Success = false, Code = "RADAR_CONFIG_UPDATE_EXCEPTION", Message = $"内部错误：{ex.Message}" },
                    _jsonOptions);
            }
        }

        /// <summary>
        /// laser-config-read — 读取激光雷达配置文件并返回最新配置
        /// </summary>
        private Task<byte[]> HandleConfigRead(byte[] payload)
        {
            try
            {
                _configHelper.ReadRadarDeviceConfig();
                return Task.FromResult(JsonSerializer.SerializeToUtf8Bytes(Program.RadarConfig, _jsonOptions));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MQTT RPC: 读取激光雷达配置时发生异常");
                var result = new CommandResult
                {
                    Success = false,
                    Code = "RADAR_CONFIG_READ_EXCEPTION",
                    Message = $"内部错误：{ex.Message}"
                };
                return Task.FromResult(JsonSerializer.SerializeToUtf8Bytes(result, _jsonOptions));
            }
        }
    }
}
